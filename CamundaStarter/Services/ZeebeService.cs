using Zeebe.Client;
using Zeebe.Client.Api.Responses;
using Zeebe.Client.Api.Worker;
using Zeebe.Client.Impl.Builder;
using NLog.Extensions.Logging;
using fastJSON;

namespace CamundaStarter.Services
{

    public interface IZeebeService
    {
       public Task<IDeployResponse> Deploy(string modelFile);
        public Task<ITopology> Status();
        public Task<string> StartWorkflowInstance(string bpmProcessId);
        public void StartWorkers();
    }

    public class MakeGreetingCustomHeadersDto
    {
        public string greeting { get; set; }
    }

    public class MakeGreetingVariablesDTO
    {
        public string name { get; set; }
    }

    public class ZeebeService : IZeebeService
    {
        private readonly IZeebeClient _client;
        private readonly ILogger<ZeebeService> _logger;

        public ZeebeService(IConfiguration config, ILogger<ZeebeService> logger)
        {
            Configuration = config;
            var zeebeUrl = Configuration["CamundaConfig:ZEEBE_ADDRESS_LOCAL"];            
            _logger = logger;

            _client =
                ZeebeClient.Builder()
                    .UseLoggerFactory(new NLogLoggerFactory())
                    .UseGatewayAddress(zeebeUrl)
                    .UsePlainText()
                    .Build();
        }
        
        public void StartWorkers()
        {
            CreateGetTimeWorker();
            CreateMakeGreetingWorker();
        }
        
        public async Task<IDeployResponse> Deploy(string modelFile)
        {
            var filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory!, "Resources", modelFile);
            try
            {
                var deployment = await _client.NewDeployCommand().AddResourceFile(filename).Send();
                var res = deployment.Processes[0];
                _logger.LogInformation("Deployed BPMN Model: " + res?.BpmnProcessId +
                            " v." + res?.Version);
                return deployment;
            }
            catch(Exception e)
            {
                _logger.LogError(message: e.ToString());
                throw;
            }
        }
        public Task<ITopology> Status()
        {
            return _client.TopologyRequest().Send();
        }

        
        public async Task<String> StartWorkflowInstance(string bpmProcessId)
        {
            _logger.LogInformation("Creating workflow instance...");
            var instance = await _client.NewCreateProcessInstanceCommand()
                .BpmnProcessId(bpmProcessId)
                .LatestVersion()
                .Variables("{\"name\": \"Nidhi Lahar\"}")
                .WithResult()
                .Send();
            var jsonParams = new JSONParameters { ShowReadOnlyProperties = true };
            return JSON.ToJSON(instance, jsonParams);
        }
        
        public void CreateGetTimeWorker()
        {
            _createWorker("get-time", async (client, job) =>
            {
                _logger.LogInformation("Received job: " + job);
                using (var httpClient = new HttpClient())
                {
                    using (var response = await httpClient.GetAsync("https://json-api.joshwulf.com/time"))
                    {
                        string apiResponse = await response.Content.ReadAsStringAsync();

                        await client.NewCompleteJobCommand(job.Key)
                            .Variables("{\"time\":" + apiResponse + "}")
                            .Send();
                        _logger.LogInformation("Get Time worker completed");
                    }
                }
            });
        }

        public void CreateMakeGreetingWorker()
        {
            _createWorker("make-greeting", async (client, job) =>
            {
                _logger.LogInformation("Make Greeting Received job: " + job);
                var headers = JSON.ToObject<MakeGreetingCustomHeadersDto>(job.CustomHeaders);
                var variables = JSON.ToObject<MakeGreetingVariablesDTO>(job.Variables);
                string greeting = headers.greeting;
                string name = variables.name;

                await client.NewCompleteJobCommand(job.Key)
                    .Variables("{\"say\": \"" + greeting + " " + name + "\"}")
                    .Send();
                _logger.LogInformation("Make Greeting Worker completed job");
            });
        }

        private void _createWorker(String jobType, JobHandler handleJob)
        {
            _client.NewWorker()
                    .JobType(jobType)
                    .Handler(handleJob)
                    .MaxJobsActive(5)
                    .Name(jobType)
                    .PollInterval(TimeSpan.FromSeconds(50))
                    .PollingTimeout(TimeSpan.FromSeconds(50))
                    .Timeout(TimeSpan.FromSeconds(10))
                    .Open();
        }
        
        private IConfiguration Configuration { get; set; }
    }
}