using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;

namespace FixStuckWorkers
{

    internal class InstanceDescription
    {
        public string InstanceId { get; private set; }
        public string Hostname { get; private set; }
        public InstanceDescription(string instanceId, string instanceHostname)
        {
            InstanceId = instanceId;
            Hostname = instanceHostname;
        }

        public InstanceDescription(Instance instance)
        {
            InstanceId = instance.InstanceId;
            Hostname = instance.PublicDnsName;
        }
    }

    internal class InstanceResult
    {
        public string Name => $"{InstanceDescription.InstanceId} - {InstanceDescription.Hostname}";
        public InstanceDescription InstanceDescription { get; private set; }
        public double Cpu { get; private set; }
        public string Reason { get; private set; }

        public InstanceResult(InstanceDescription instanceDescription, string reason, double cpu)
        {
            InstanceDescription = instanceDescription;
            Cpu = cpu;
            Reason = reason;
        }
    }

    internal class CommandOptions
    {
        public CommandOption AccessKey { get; set; }
        public CommandOption SecretKey { get; set; }
        public CommandOption Name { get; set; }
        public CommandOption Region { get; set; }
        public CommandOption SecurityGroup { get; set; }
        public CommandOption ShowGoodInstances { get; set; }
        public CommandOption ShowUnknownInstances { get; set; }
        public CommandOption IsClassic { get; set; }
    }

    internal class InstanceResults
    {
        public ConcurrentBag<InstanceResult> BadInstances { get; set; }
        public ConcurrentBag<InstanceResult> GoodInstances { get; set; }
        public ConcurrentBag<InstanceResult> UnknownInstances { get; set; }
        public InstanceResults()
        {
            BadInstances = new ConcurrentBag<InstanceResult>();
            GoodInstances = new ConcurrentBag<InstanceResult>();
            UnknownInstances = new ConcurrentBag<InstanceResult>();
        }
    }
    public class Program
    {
        private const double MaxBadCpuPercent = 1.0;
        private const double UnknownCpuPercent = -1;
        private const double MinStalledCpuPercent = 5.0;
        private const double MaxStdDevStalledCpuPercent = 0.05;
        private const int MaxMetricConcurrency = 8;
        private const int MetricRetrievalInHours = 3;
        private const int PeriodInMinutes = 10;
        //require at least 6 periods (60 minutes) to judge good or bad
        private const int MinServerMetricsToJudge = 6;

        private static string AccessKey { get; set; }
        private static string SecretKey { get; set; }
        private static string Name { get; set; }
        private static RegionEndpoint Region { get; set; }
        private static string SecurityGroup { get; set; }
        private static bool ShowGoodInstances { get; set; }
        private static bool ShowUnknownInstances { get; set; }
        private static bool IsClassic { get; set; }

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication
            {
                Name = "find-idle-instances",
                Description = string.Format("Identifies and optionally terminates idle workers. Idle is defined as less than {0}% CPU for {1} hours.",
                    MaxBadCpuPercent, MetricRetrievalInHours),
                FullName = "find-idle-instances - fixing your idle AWS servers since 2016"
            };


            //if no command - show help
            app.OnExecute(() =>
            {
                app.ShowHelp();
                return 2;
            });

            app.Command("find", c =>
            {
                c.Description = "Finds stuck instances";
                var commandOptions = GetOptions(c);

                c.OnExecute(async () =>
                {
                    bool success = ValidateAndSetOptions(commandOptions);
                    if (!success)
                    {
                        c.ShowHelp("find");
                        return 1;
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Running ({0})...", Name);
                    Console.ForegroundColor = ConsoleColor.White;

                    var instanceResults = await FindInstancesAndCategorize();
                    DisplayInstanceResults(instanceResults);

                    return 0;
                });


            });
            app.Command("terminate", c =>
            {
                c.Description = "Terminates stuck instances.";
                var commandOptions = GetOptions(c);

                c.OnExecute(async () =>
                {
                    bool success = ValidateAndSetOptions(commandOptions);
                    if (!success)
                    {
                        c.ShowHelp("terminate");
                        return 1;
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Running ({0})...", Name);
                    Console.ForegroundColor = ConsoleColor.White;

                    var instanceResults = await FindInstancesAndCategorize();
                    DisplayInstanceResults(instanceResults);

                    if (instanceResults.BadInstances.Count == 0)
                    {
                        Console.WriteLine("No bad instances present.");
                        return 0;
                    }

                    //confirm terminate terminate
                    Console.WriteLine();
                    Console.WriteLine("Terminate Instances? (y/n)  ");
                    char termChar = 'x';
                    while (termChar != 'y' && termChar != 'n')
                    {
                        termChar = Console.ReadKey().KeyChar;
                    }
                    if (termChar != 'y')
                    {
                        return 0;
                    }

                    //terminate instances
                    await TerminateInstances(instanceResults.BadInstances.ToList());



                    return 0;
                });
            });

            app.Command("reboot", c =>
            {
                c.Description = "Reboot stuck instances.";
                var commandOptions = GetOptions(c);

                c.OnExecute(async () =>
                {
                    bool success = ValidateAndSetOptions(commandOptions);
                    if (!success)
                    {
                        c.ShowHelp("reboot");
                        return 1;
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Running ({0})...", Name);
                    Console.ForegroundColor = ConsoleColor.White;

                    var instanceResults = await FindInstancesAndCategorize();
                    DisplayInstanceResults(instanceResults);

                    if (instanceResults.BadInstances.Count == 0)
                    {
                        Console.WriteLine("No bad instances present.");
                        return 0;
                    }

                    //confirm terminate terminate
                    Console.WriteLine();
                    Console.WriteLine("Reboot Instances? (y/n)  ");
                    char termChar = 'x';
                    while (termChar != 'y' && termChar != 'n')
                    {
                        termChar = Console.ReadKey().KeyChar;
                    }
                    if (termChar != 'y')
                    {
                        return 0;
                    }

                    //terminate instances
                    await RebootInstances(instanceResults.BadInstances.ToList());



                    return 0;
                });
            });
            return app.Execute(args);
        }
        private static CommandOptions GetOptions(CommandLineApplication c)
        {
            return new CommandOptions
            {
                AccessKey = c.Option("--accesskey", "AWS Access Key (required)", CommandOptionType.SingleValue),
                SecretKey = c.Option("--secretkey", "AWS Secret Key (required)", CommandOptionType.SingleValue),
                Name = c.Option("--name", "Name of worker to search on (required)", CommandOptionType.SingleValue),
                Region = c.Option("--region", "AWS region name (optional)", CommandOptionType.SingleValue),
                SecurityGroup = c.Option("--securityGroup", "SecurityGroup name associated with the instance (Optional but recommended!)", CommandOptionType.SingleValue),
                ShowGoodInstances = c.Option("--show-good", "Display 'good' instances in results (optional)", CommandOptionType.NoValue),
                ShowUnknownInstances = c.Option("--show-unknown", "Display 'unknown' instances in results (optional)", CommandOptionType.NoValue),
                IsClassic = c.Option("--IsClassic", "Look for instances that are not in a VPC (optional)", CommandOptionType.NoValue)
            };
        }

        private static bool ValidateAndSetOptions(CommandOptions options)
        {
            if (!options.AccessKey.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("AccessKey is required.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
            if (!options.SecretKey.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("SecretKey is required.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
            if (!options.Name.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Name is required.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }


            AccessKey = options.AccessKey.Value();
            SecretKey = options.SecretKey.Value();
            Name = options.Name.Value();
            ShowGoodInstances = options.ShowGoodInstances.HasValue();
            ShowUnknownInstances = options.ShowUnknownInstances.HasValue();
            IsClassic = options.IsClassic.HasValue();

            Region = string.IsNullOrWhiteSpace(options.Region.Value()) ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(options.Region.Value());
            SecurityGroup = string.IsNullOrWhiteSpace(options.SecurityGroup.Value()) ? null : options.SecurityGroup.Value().Trim();
            return true;
        }

        private static async Task<InstanceResults> FindInstancesAndCategorize()
        {
            var instances = await FindInstances();
            AmazonCloudWatchClient cwClient = new AmazonCloudWatchClient(AccessKey, SecretKey, Region);

            var instanceResults = new InstanceResults();

            //this was written this way to facilite better control over throttling (vs Parallel.ForEach with MaxConcurrency)
            //in addition a future enhancement could be batching instances to GetMatricStatistics call
            await instances.ForEachAsync(MaxMetricConcurrency, async (instance) =>
            {
                //var instance = instances[i];

                var dim = new Dimension()
                {
                    Name = "InstanceId",
                    Value = instance.InstanceId,
                };

                var request = new GetMetricStatisticsRequest()
                {
                    Dimensions = new List<Dimension> { dim },
                    Namespace = "AWS/EC2",
                    MetricName = "CPUUtilization",
                    Unit = StandardUnit.Percent,
                    Statistics = new List<string>() { "Average" },
                    StartTime = DateTime.UtcNow.Subtract(TimeSpan.FromHours(MetricRetrievalInHours)),
                    EndTime = DateTime.UtcNow,
                    Period = (int)TimeSpan.FromMinutes(PeriodInMinutes).TotalSeconds,
                };
                var response = await cwClient.GetMetricStatisticsAsync(request);
                var max = UnknownCpuPercent;
                var avg = 0.0d;
                var stdDev = 0.0d;
                var count = response.Datapoints.Count;
                if (count >= MinServerMetricsToJudge)
                {
                    // first loop to calculate max and sum
                    var sum = 0.0d;
                    foreach (var dp in response.Datapoints)
                    {
                        if (dp.Average > max)
                        {
                            max = dp.Average;
                        }
                        sum += dp.Average;
                    }
                    avg = sum / count;

                    // second loop allows us to calculate std deviation
                    var varianceSum = 0.0d;
                    foreach (var dp in response.Datapoints)
                    {
                        varianceSum += Math.Pow(dp.Average - avg, 2.0d);
                    }
                    stdDev = Math.Sqrt(varianceSum / count);
                }

                if (max == UnknownCpuPercent)
                {
                    instanceResults.UnknownInstances.Add(new InstanceResult(instance, "Unknown CPU", max));
                }
                else if (max < MaxBadCpuPercent)
                {
                    // if the CPU is basically doing nothing
                    //Console.WriteLine($"LOW  {instance.Hostname}\tMax={max}, Avg={avg}, StdDev={stdDev}");
                    instanceResults.BadInstances.Add(new InstanceResult(instance, "Low CPU", max));
                }
                else if (max > MinStalledCpuPercent && stdDev < MaxStdDevStalledCpuPercent)
                {
                    // if the CPU is above a floor (MinStalledCpuPercent) but is essentially flatlined
                    //Console.WriteLine($"FLAT {instance.Hostname}\tMax={max}, Avg={avg}, StdDev={stdDev}");
                    instanceResults.BadInstances.Add(new InstanceResult(instance, "Flatlined CPU", stdDev));
                }
                else
                {
                    instanceResults.GoodInstances.Add(new InstanceResult(instance, null, max));
                }

                var processed = instanceResults.BadInstances.Count + instanceResults.UnknownInstances.Count +
                    instanceResults.GoodInstances.Count;

                Console.Write("\rProcessed {0}/{1} instances.", processed, instances.Count);

                //depending on your AWS setup you might need to throttle these calls more aggressively
                //Thread.Sleep(50);
            });

            Console.WriteLine();
            return instanceResults;
        }

        private static async Task<List<InstanceDescription>> FindInstances()
        {
            AmazonEC2Client client = new AmazonEC2Client(AccessKey, SecretKey, Region);

            var filters = new List<Filter>()
            {
                new Filter("tag:Name", new List<string> { Name }),
                new Filter("instance-state-name",new List<string> { "running" }),
            };
            if (!string.IsNullOrWhiteSpace(SecurityGroup))
            {
                if (IsClassic)
                {
                    filters.Add(new Filter("group-name", new List<string> { SecurityGroup }));
                }
                else
                {
                    filters.Add(new Filter("instance.group-name", new List<string> { SecurityGroup }));
                }
            }

            //TODO: support paging with tokens
            var request = new DescribeInstancesRequest()
            {
                Filters = filters
            };
            var response = await client.DescribeInstancesAsync(request);
            var instances = response.Reservations.SelectMany(r => r.Instances).Select(i => new InstanceDescription(i)).ToList();
            Console.WriteLine();
            Console.WriteLine("Found {0} matching instances.", instances.Count);
            return instances;
        }

        private static void DisplayInstanceResults(InstanceResults results)
        {
            var hasBadInstances = results.BadInstances != null && results.BadInstances.Count > 0;
            if (ShowGoodInstances || ShowUnknownInstances || hasBadInstances)
            {
                Console.WriteLine("");
            }
            if (ShowGoodInstances)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                WriteStat($"Good Instances ({results.GoodInstances.Count})", "CPU%");
                Console.WriteLine("=====================================================================================");
                foreach (var result in results.GoodInstances)
                {
                    WriteStat(result.Name, result.Cpu);
                }
                Console.WriteLine("");
            }
            if (ShowUnknownInstances)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                WriteStat($"Unknown Instances ({results.UnknownInstances.Count})", "CPU%");
                Console.WriteLine("=================================================================================");
                foreach (var result in results.UnknownInstances)
                {
                    WriteStat(result.Name, result.Cpu);
                }
                Console.WriteLine("");
            }

            if (hasBadInstances)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                WriteStat($"Bad Instances ({results.BadInstances.Count})", "Reason", "CPU%");
                Console.WriteLine("===================================================================================================");
                foreach (var result in results.BadInstances)
                {
                    WriteStat(result.Name, result.Reason, result.Cpu);
                }
                Console.WriteLine("");
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        private static void WriteStat(string label, double stat)
        {
            WriteStat(label, stat.ToString("##0.00"));
        }

        private static void WriteStat(string label, string stat)
        {
            Console.Write(label);
            Console.CursorLeft = (85 - stat.Length);
            Console.Write(stat);
            Console.WriteLine();
        }

        private static void WriteStat(string label, string reason, double stat)
        {
            WriteStat(label, reason, stat.ToString("##0.00"));
        }

        private static void WriteStat(string label, string reason, string stat)
        {
            Console.Write(label);
            Console.CursorLeft = 80;
            Console.Write(reason);
            Console.CursorLeft = (99 - stat.Length);
            Console.Write(stat);
            Console.WriteLine();
        }

        private static async Task TerminateInstances(List<InstanceResult> badInstances)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Terminating Instances!");
            Console.WriteLine("----------------------");
            badInstances.ForEach(i => Console.WriteLine(i.Name));
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;

            AmazonEC2Client client = new AmazonEC2Client(AccessKey, SecretKey, Region);

            //go sync so we can take it slower
            int processed = 0;
            foreach (var instance in badInstances.Select(i => i.InstanceDescription.InstanceId))
            {
                var request = new TerminateInstancesRequest()
                {
                    InstanceIds = new List<string> { instance },
                };
                var response = await client.TerminateInstancesAsync(request);

                Console.Write("\rProcessed {0}/{1} instances.", ++processed, badInstances.Count);
            }
            //TODO: support paging with tokens

            Console.WriteLine();
            Console.WriteLine("Complete");

        }

        private static async Task RebootInstances(List<InstanceResult> badInstances)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Rebooting Instances!");
            Console.WriteLine("----------------------");
            badInstances.ForEach(i => Console.WriteLine(i.Name));
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;

            AmazonEC2Client client = new AmazonEC2Client(AccessKey, SecretKey, Region);

            //go sync so we can take it slower
            //if this list starts to get too large we could easily do async in batches
            int processed = 0;
            foreach (var instance in badInstances.Select(i => i.InstanceDescription.InstanceId))
            {
                var request = new RebootInstancesRequest()
                {
                    InstanceIds = new List<string> { instance },
                };
                var response = await client.RebootInstancesAsync(request);

                Console.Write("\rProcessed {0}/{1} instances.", ++processed, badInstances.Count);
            }

            Console.WriteLine();
            Console.WriteLine("Complete");

        }
    }
    internal static class AsyncHelpers
    {
        //from: http://blogs.msdn.com/b/pfxteam/archive/2012/03/05/10278165.aspx
        internal static Task ForEachAsync<T>(
            this IEnumerable<T> source, int dop, Func<T, Task> body)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(dop)
                select Task.Run(async delegate
                {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current).ContinueWith(t =>
                            {
                                //observe exceptions
                            });

                }));
        }
    }


}
