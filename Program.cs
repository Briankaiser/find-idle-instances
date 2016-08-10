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
    internal class InstanceResult
    {
        public string Name { get; private set; }
        public double Cpu { get; private set; }
        public InstanceResult(string name, double cpu)
        {
            Name = name;
            Cpu = cpu;
        }
        public override string ToString()
        {
            return Name + "\t\t" + Cpu.ToString("0.00");
        }
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
        private const int MaxMetricConcurrency = 8;
        private const int MetricRetrievalInHours = 3;
        private const int PeriodInMinutes = 10;
        //require at least 6 periods (60 minutes) to judge good or bad
        private const int MinServerMetricsToJudge = 6;

        private static string AccessKey { get; set; }
        private static string SecretKey { get; set; }
        private static string Name { get; set; }
        private static RegionEndpoint Region { get; set; }
        private static bool ShowGoodInstances { get; set; }
        private static bool ShowUnknownInstances { get; set; }

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
                var accessOption = c.Option("--accesskey", "AWS Access Key (required)", CommandOptionType.SingleValue);
                var secretOption = c.Option("--secretkey", "AWS Secret Key (required)", CommandOptionType.SingleValue);
                var nameOption = c.Option("--name", "Name of worker to search on (required)", CommandOptionType.SingleValue);
                var regionOption = c.Option("--region", "AWS region name (optional)", CommandOptionType.SingleValue);
                var showGoodOption = c.Option("--show-good", "Display 'good' instances in results (optional)", CommandOptionType.NoValue);
                var showUnknownOption = c.Option("--show-unknown", "Display 'unknown' instances in results (optional)", CommandOptionType.NoValue);

                c.OnExecute(async () =>
                {
                    bool success = ValidateAndSetOptions(accessOption, secretOption, nameOption, regionOption, showGoodOption, showUnknownOption);
                    if(!success)
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
                var accessOption = c.Option("--accesskey", "AWS Access Key (required)", CommandOptionType.SingleValue);
                var secretOption = c.Option("--secretkey", "AWS Secret Key (required)", CommandOptionType.SingleValue);
                var nameOption = c.Option("--name", "Name of worker to search on (required)", CommandOptionType.SingleValue);
                var regionOption = c.Option("--region", "AWS region name (optional)", CommandOptionType.SingleValue);
                var showGoodOption = c.Option("--show-good", "Display 'good' instances in results (optional)", CommandOptionType.NoValue);
                var showUnknownOption = c.Option("--show-unknown", "Display 'unknown' instances in results (optional)", CommandOptionType.NoValue);

                c.OnExecute(async () =>
                {
                    bool success = ValidateAndSetOptions(accessOption, secretOption, nameOption, regionOption, showGoodOption, showUnknownOption);
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

                    if(instanceResults.BadInstances.Count == 0)
                    {
                        Console.WriteLine("No bad instances present.");
                        return 0;
                    }

                    //confirm terminate terminate
                    Console.WriteLine();
                    Console.WriteLine("Terminate Instances? (y/n)  ");
                    char termChar = 'x';
                    while(termChar != 'y' && termChar != 'n')
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
                var accessOption = c.Option("--accesskey", "AWS Access Key (required)", CommandOptionType.SingleValue);
                var secretOption = c.Option("--secretkey", "AWS Secret Key (required)", CommandOptionType.SingleValue);
                var nameOption = c.Option("--name", "Name of worker to search on (required)", CommandOptionType.SingleValue);
                var regionOption = c.Option("--region", "AWS region name (optional)", CommandOptionType.SingleValue);
                var showGoodOption = c.Option("--show-good", "Display 'good' instances in results (optional)", CommandOptionType.NoValue);
                var showUnknownOption = c.Option("--show-unknown", "Display 'unknown' instances in results (optional)", CommandOptionType.NoValue);

                c.OnExecute(async () =>
                {
                    bool success = ValidateAndSetOptions(accessOption, secretOption, nameOption, regionOption, showGoodOption, showUnknownOption);
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
        private static bool ValidateAndSetOptions(CommandOption accessOption, CommandOption secretOption, CommandOption nameOption,
            CommandOption regionOption, CommandOption showGoodOption, CommandOption showUnknownOption)
        {
            if (!accessOption.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("AccessKey is required.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
            if (!secretOption.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("SecretKey is required.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
            if (!nameOption.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Name is required.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }


            AccessKey = accessOption.Value();
            SecretKey = secretOption.Value();
            Name = nameOption.Value();
            ShowGoodInstances = showGoodOption.HasValue();
            ShowUnknownInstances = showUnknownOption.HasValue();

            Region = string.IsNullOrWhiteSpace(regionOption.Value()) ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(regionOption.Value());

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
                    Value = instance,
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
                var max = response.Datapoints.Count < MinServerMetricsToJudge ? UnknownCpuPercent : response.Datapoints.Max(p => p.Average);

                if (max == UnknownCpuPercent)
                {
                    instanceResults.UnknownInstances.Add(new InstanceResult(instance, max));
                }
                else if (max < MaxBadCpuPercent)
                {
                    instanceResults.BadInstances.Add(new InstanceResult(instance, max));
                }
                else
                {
                    instanceResults.GoodInstances.Add(new InstanceResult(instance, max));
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

        private static async Task<List<string>> FindInstances()
        {
            AmazonEC2Client client = new AmazonEC2Client(AccessKey, SecretKey, Region);

            //TODO: support paging with tokens
            var request = new DescribeInstancesRequest()
            {
               Filters = new List<Filter>()
               {
                   new Filter("tag:Name", new List<string> { Name }),
                   new Filter("instance-state-name",new List<string> { "running" }),
               },
            };
            var response = await client.DescribeInstancesAsync(request);
            var instances = response.Reservations.SelectMany(r => r.Instances).Select(i => i.InstanceId).ToList();
            Console.WriteLine();
            Console.WriteLine("Found {0} matching instances.", instances.Count);
            return instances;
        }

        private static void DisplayInstanceResults(InstanceResults results)
        {
            Console.WriteLine("");
            if (ShowGoodInstances)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Good Instances ({0})\tCPU%", results.GoodInstances.Count);
                Console.WriteLine("============================");
                foreach (var result in results.GoodInstances)
                {
                    Console.WriteLine(result);
                }
                Console.WriteLine("");
            }
            if (ShowUnknownInstances)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Unknown Instances ({0})\tCPU%", results.UnknownInstances.Count);
                Console.WriteLine("============================");
                foreach (var result in results.UnknownInstances)
                {

                    Console.WriteLine(result);
                }
                Console.WriteLine("");
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Bad Instances ({0})\tCPU%", results.BadInstances.Count);
            Console.WriteLine("============================");
            foreach (var result in results.BadInstances)
            {
                Console.WriteLine( result);
            }
            Console.WriteLine("");

            Console.ForegroundColor = ConsoleColor.White;

        }

        private static async Task TerminateInstances(List<InstanceResult> badInstances )
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
            foreach(var instance in badInstances.Select(i=>i.Name))
            {
                var request = new TerminateInstancesRequest()
                {
                    InstanceIds = new List<string> {  instance},
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
            foreach (var instance in badInstances.Select(i => i.Name))
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
                select Task.Run(async delegate {
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
