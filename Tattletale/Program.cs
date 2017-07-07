using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Tattletale
{

    public class AppSettings
    {
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string Region { get; set; }
        public int Interval { get; set; }
        public List<Domain> Domains { get; set; }
    }

    public class Domain
    {
        public string HostedZoneID { get; set; }
        public string Name { get; set; }
    }

    public class Program
    {
        private static readonly Uri uri = new Uri("https://api.ipify.org");
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly ILogger logger = new LoggerConfiguration()
            .WriteTo.ColoredConsole()
            .CreateLogger();

        private static AmazonRoute53Client client;

        public static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("Settings.json", true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            var manualResetEvent = new ManualResetEvent(false);

            var settings = configuration.GetSection("App").Get<AppSettings>();

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                manualResetEvent.Set();
            };

            client = new AmazonRoute53Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), RegionEndpoint.GetBySystemName(settings.Region));

            var timer = new Timer(Update, settings, 1000, settings.Interval);

            Console.WriteLine("Application started. Press Ctrl+C to shut down.");

            manualResetEvent.WaitOne();
        }

        private static void Update(object state)
        {
            try
            {
                logger.Information("");

                Update((AppSettings)state).Wait();
            }
            catch (Exception exception)
            {
                logger.Fatal(exception, "");
            }
        }

        private static async Task<string> GetIP()
        {
            try
            {
                return await httpClient.GetStringAsync(uri);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "");
                throw;
            }
        }

        private static async Task<ResourceRecordSet> GetResourceRecordSet(string hostedZoneID, string domainName)
        {
            try
            {
                var listResourceRecordSetsRequest = new ListResourceRecordSetsRequest(hostedZoneID);

                var listResourceRecordSetsResponse = await client.ListResourceRecordSetsAsync(listResourceRecordSetsRequest);

                return listResourceRecordSetsResponse.ResourceRecordSets.SingleOrDefault(x => x.Name == domainName && x.Type == RRType.A);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "");
                throw;
            }
        }

        private static async Task Update(AppSettings settings)
        {
            var ip = await GetIP();

            foreach (var domain in settings.Domains)
            {
                var resourceRecordSet = await GetResourceRecordSet(domain.HostedZoneID, domain.Name);

                var resourceRecord = resourceRecordSet?.ResourceRecords.FirstOrDefault();

                if (resourceRecord == null)
                {
                    return;
                }

                if (resourceRecord.Value == ip)
                {
                    return;
                }

                resourceRecord.Value = ip;

                await UpdateResourceRecordSet(resourceRecordSet, domain);
            }
        }

        private static async Task UpdateResourceRecordSet(ResourceRecordSet resourceRecordSet, Domain domain)
        {
            var change = new Change(ChangeAction.UPSERT, resourceRecordSet);

            var changeBatch = new ChangeBatch(new List<Change> {change});

            var request = new ChangeResourceRecordSetsRequest(domain.HostedZoneID, changeBatch);

            //var response = await client.ChangeResourceRecordSetsAsync(request);
        }
    }
}