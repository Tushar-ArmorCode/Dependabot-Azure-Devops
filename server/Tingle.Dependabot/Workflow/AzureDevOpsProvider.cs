﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Tingle.Dependabot.Models.Azure;
using Tingle.Dependabot.Models.Management;

namespace Tingle.Dependabot.Workflow;

public class AzureDevOpsProvider // TODO: replace the Microsoft.(TeamFoundation|VisualStudio) libraries with direct usage of HttpClient
{
    // Possible/allowed paths for the configuration files in a repository.
    private static readonly IReadOnlyList<string> ConfigurationFilePaths = new[] {
        // TODO: restore checks in .azuredevops folder once either the code can check that folder or we are passing ignore conditions via update_jobs API
        //".azuredevops/dependabot.yml",
        //".azuredevops/dependabot.yaml",

        ".github/dependabot.yml",
        ".github/dependabot.yaml",
    };

    private static readonly (string, string)[] SubscriptionEventTypes =
    {
        ("git.push", "1.0"),
        ("git.pullrequest.updated", "1.0"),
        ("git.pullrequest.merged", "1.0"),
        ("ms.vss-code.git-pullrequest-comment-event", "2.0"),
    };

    private readonly IMemoryCache cache;
    private readonly HttpClient httpClient = new(); // TODO: consider injecting this for logging and tracing purposes
    private readonly WorkflowOptions options;

    public AzureDevOpsProvider(IMemoryCache cache, IOptions<WorkflowOptions> optionsAccessor)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        options = optionsAccessor?.Value ?? throw new ArgumentNullException(nameof(optionsAccessor));
    }

    public async Task<List<string>> CreateOrUpdateSubscriptionsAsync(Project project, CancellationToken cancellationToken = default)
    {
        // prepare the query
        var projectId = project.ProviderId ?? throw new InvalidOperationException("ProviderId for the project cannot be null");
        var query = new AzdoSubscriptionsQuery
        {
            PublisherId = "tfs",
            PublisherInputFilters = new List<AzdoSubscriptionsQueryInputFilter>
            {
                new AzdoSubscriptionsQueryInputFilter
                {
                    Conditions = new List<AzdoSubscriptionsQueryInputFilterCondition>
                    {
                        new AzdoSubscriptionsQueryInputFilterCondition
                        {
                            InputId = "projectId",
                            Operator = AzdoSubscriptionsQueryInputFilterOperator.Equals,
                            InputValue = projectId,
                        },
                    },
                },
            },

            ConsumerId = "webHooks",
            ConsumerActionId = "httpRequest",
        };

        // fetch the subscriptions
        var url = (AzureDevOpsProjectUrl)project.Url!;
        var uri = new UriBuilder
        {
            Scheme = url.Scheme,
            Host = url.Hostname,
            Port = url.Port ?? -1,
            Path = $"{url.OrganizationName}/_apis/hooks/subscriptionsquery",
            Query = "?api-version=7.0",
        }.Uri;
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(query), };
        var subscriptions = (await SendAsync<AzdoSubscriptionsQueryResponse>(project.Token!, request, cancellationToken)).Results;

        // iterate each subscription checking if creation or update is required
        var webhookUrl = options.WebhookEndpoint!;
        var ids = new List<string>();
        foreach (var (eventType, resourceVersion) in SubscriptionEventTypes)
        {
            // find an existing one
            AzdoSubscription? existing = null;
            foreach (var sub in subscriptions)
            {
                if (sub.EventType == eventType
                    && sub.ConsumerInputs.TryGetValue("url", out var rawUrl)
                    && webhookUrl == new Uri(rawUrl)) // comparing with Uri ensure we don't have to deal with slashes and default ports
                {
                    existing = sub;
                    break;
                }
            }

            // if we have an existing one, update it, otherwise create a new one

            if (existing is not null)
            {
                // publisherId, consumerId, and consumerActionId cannot be updated
                existing.EventType = eventType;
                existing.ResourceVersion = resourceVersion;
                existing.PublisherInputs = MakeTfsPublisherInputs(eventType, projectId);
                existing.ConsumerInputs = MakeWebHooksConsumerInputs(project, webhookUrl);
                uri = new UriBuilder(uri) { Path = $"{url.OrganizationName}/_apis/hooks/subscriptions/{existing.Id}", }.Uri;
                request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = JsonContent.Create(existing), };
                existing = await SendAsync<AzdoSubscription>(project.Token!, request, cancellationToken);
            }
            else
            {
                existing = new AzdoSubscription
                {
                    EventType = eventType,
                    ResourceVersion = resourceVersion,

                    PublisherId = "tfs",
                    PublisherInputs = MakeTfsPublisherInputs(eventType, projectId),
                    ConsumerId = "webHooks",
                    ConsumerActionId = "httpRequest",
                    ConsumerInputs = MakeWebHooksConsumerInputs(project, webhookUrl),
                };
                uri = new UriBuilder(uri) { Path = $"{url.OrganizationName}/_apis/hooks/subscriptions", }.Uri;
                request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(existing), };
                existing = await SendAsync<AzdoSubscription>(project.Token!, request, cancellationToken);
            }

            // track the identifier of the subscription
            ids.Add(existing.Id!);
        }

        return ids;
    }

    public async Task<AzdoProject> GetProjectAsync(Project project, CancellationToken cancellationToken)
    {
        var url = (AzureDevOpsProjectUrl)project.Url!;
        var uri = new UriBuilder
        {
            Scheme = url.Scheme,
            Host = url.Hostname,
            Port = url.Port ?? -1,
            Path = $"{url.OrganizationName}/_apis/projects/{url.ProjectIdOrName}",
            Query = "?api-version=7.0",
        }.Uri;
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync<AzdoProject>(project.Token!, request, cancellationToken);
    }

    public async Task<List<AzdoRepository>> GetRepositoriesAsync(Project project, CancellationToken cancellationToken)
    {
        var url = (AzureDevOpsProjectUrl)project.Url!;
        var uri = new UriBuilder
        {
            Scheme = url.Scheme,
            Host = url.Hostname,
            Port = url.Port ?? -1,
            Path = $"{url.OrganizationName}/{url.ProjectIdOrName}/_apis/git/repositories",
            Query = "?api-version=7.0",
        }.Uri;
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var data = await SendAsync<AzdoListResponse<AzdoRepository>>(project.Token!, request, cancellationToken);
        return data.Value;
    }

    public async Task<AzdoRepository> GetRepositoryAsync(Project project, string repositoryIdOrName, CancellationToken cancellationToken)
    {
        var url = (AzureDevOpsProjectUrl)project.Url!;
        var uri = new UriBuilder
        {
            Scheme = url.Scheme,
            Host = url.Hostname,
            Port = url.Port ?? -1,
            Path = $"{url.OrganizationName}/{url.ProjectIdOrName}/_apis/git/repositories/{repositoryIdOrName}",
            Query = "?api-version=7.0",
        }.Uri;
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync<AzdoRepository>(project.Token!, request, cancellationToken);
    }

    public async Task<GitItem?> GetConfigurationFileAsync(Project project, string repositoryIdOrName, CancellationToken cancellationToken = default)
    {
        // get a connection to Azure DevOps
        var url = (AzureDevOpsProjectUrl)project.Url!;
        var connection = CreateVssConnection(url, project.Token!);

        // Try all known paths
        var client = await connection.GetClientAsync<GitHttpClient>(cancellationToken);
        foreach (var path in ConfigurationFilePaths)
        {
            try
            {
                var item = await client.GetItemAsync(project: url.ProjectIdOrName,
                                                     repositoryId: repositoryIdOrName,
                                                     path: path,
                                                     latestProcessedChange: true,
                                                     includeContent: true,
                                                     cancellationToken: cancellationToken);

                if (item is not null) return item;
            }
            catch (VssServiceException) { }
        }

        return null;
    }

    private VssConnection CreateVssConnection(AzureDevOpsProjectUrl url, string token)
    {
        static string hash(string v)
        {
            var bytes = Encoding.UTF8.GetBytes(v);
            var hash = SHA256.HashData(bytes);
            return BitConverter.ToString(hash).Replace("-", "");
        }

        // The cache key uses the project URL in case the token is different per project.
        // It also, uses the token to ensure a new connection if the token is updated.
        // The token is hashed to avoid exposing it just in case it is exposed.
        var cacheKey = $"vss_connections:{hash($"{url}{token}")}";
        var cached = cache.Get<VssConnection>(cacheKey);
        if (cached is not null) return cached;

        var uri = new Uri(url.OrganizationUrl);
        var creds = new VssBasicCredential(string.Empty, token);
        cached = new VssConnection(uri, creds);

        return cache.Set(cacheKey, cached, TimeSpan.FromHours(1));
    }

    private async Task<T> SendAsync<T>(string token, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}")));

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken))!;
    }

    internal static Dictionary<string, string> MakeTfsPublisherInputs(string type, string projectId)
    {
        // possible inputs are available via an authenticated request to
        // https://dev.azure.com/{organization}/_apis/hooks/publishers/tfs

        // always include the project identifier, to restrict events from that project
        var result = new Dictionary<string, string> { ["projectId"] = projectId, };

        if (type is "git.pullrequest.updated")
        {
            result["notificationType"] = "StatusUpdateNotification";
        }

        if (type is "git.pullrequest.merged")
        {
            result["mergeResult"] = "Conflicts";
        }

        return result;
    }
    internal static Dictionary<string, string> MakeWebHooksConsumerInputs(Project project, Uri webhookUrl)
    {
        return new Dictionary<string, string>
        {
            // possible inputs are available via an authenticated request to
            // https://dev.azure.com/{organization}/_apis/hooks/consumers/webHooks

            ["detailedMessagesToSend"] = "none",
            ["messagesToSend"] = "none",
            ["url"] = webhookUrl.ToString(),
            ["basicAuthUsername"] = project.Id!,
            ["basicAuthPassword"] = project.Password!,
        };
    }
}
