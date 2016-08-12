# find-idle-instances
A small utility to identify instances which have been running idle and could be termianted. 
Initially created to find Hudl servers that failed some aspect of signup and as in a 'stuck' state. 

After build the application can be run with `.\find-idle-instances` . 

Help on commands is built into the command line parser. For example `.\find-idle-instances help find` will show options for the 'find' command.
For all commands `--accesskey`, `--secretkey`, `--name` are required. 
Name is a search string on the AWS property `tag:Name`. Examples are "\*" (for all servers) or "prod-farm-stream\*" for all servers of a particular group

# Run with Docker
`docker build -t <name> .`

Finding instances - `docker run <name> find --accesskey #### --secretkey ##### --name <workerType>`
