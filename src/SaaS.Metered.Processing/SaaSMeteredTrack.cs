using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;
using SaaS.Metered.Processing.Model;

namespace SaaS.Metering.Processing
{
    public static class SaaSMeteredTrack
    {
        private static CosmosClient client = new CosmosClient(GetEnvironmentVariable("CosmosDb_Uri"), GetEnvironmentVariable("CosmosDb_Key"));
        [FunctionName("SaaSMeteringTrack")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "addmeteredprocessing")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string subscriptionId = req.Query["subscriptionId"];
            string dimensionId = req.Query["dimensionId"];
            string planId = req.Query["planId"];




            MeteredItem item = new MeteredItem();
            item.SubscriptionId = subscriptionId;
            item.DimensionId = dimensionId;
            item.PlanId = planId;
            item.MeterProcessStatus = 0;
            item.CreatedDate = DateTime.Now;
            item.id = Guid.NewGuid().ToString();
                
             
            var collectionId = GetEnvironmentVariable("CosmosDb_Collection");
            var databaseId = GetEnvironmentVariable("CosmosDb_Database");

            try
            {
                ItemResponse<MeteredItem> response = await client.GetContainer(databaseId, collectionId).UpsertItemAsync(item, new PartitionKey(item.id));
                log.LogInformation("Document created in Cosmos: " + response.StatusCode);
            }
            catch (CosmosException ex)
            {
                log.LogInformation("error created in Cosmos: " + ex.Message);
                log.LogInformation("Add Object to Retry Queue: " + ex.Message);
  
            }


            log.LogInformation($"C# HTTP trigger function inserted one row");
            return new OkObjectResult("Metered Item recorded");
        }

        public static string GetEnvironmentVariable(string variableName)
        {
            return Environment.GetEnvironmentVariable(variableName);
        }
    }




}
