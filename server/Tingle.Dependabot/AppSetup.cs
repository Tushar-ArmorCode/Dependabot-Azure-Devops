﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;
using Tingle.Dependabot.Models;
using Tingle.Dependabot.Workflow;

namespace Tingle.Dependabot;

internal static class AppSetup
{
    private class ProjectSetupInfo
    {
        public required Uri Url { get; set; }
        public required string Token { get; set; }
        public bool AutoComplete { get; set; }
        public List<int>? AutoCompleteIgnoreConfigs { get; set; }
        public MergeStrategy? AutoCompleteMergeStrategy { get; set; }
        public bool AutoApprove { get; set; }
        public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task SetupAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;

        // perform migrations on startup if asked to
        if (app.Configuration.GetValue<bool>("EFCORE_PERFORM_MIGRATIONS"))
        {
            var db = provider.GetRequiredService<MainDbContext>().Database;
            if (db.IsRelational()) // only relational databases
            {
                await db.MigrateAsync(cancellationToken: cancellationToken);
            }
        }

        // add project if there are projects to be added
        var adoProvider = provider.GetRequiredService<AzureDevOpsProvider>();
        var context = provider.GetRequiredService<MainDbContext>();
        var projects = await context.Projects.ToListAsync(cancellationToken);
        var setupsJson = app.Configuration.GetValue<string?>("PROJECT_SETUPS");
        if (!string.IsNullOrWhiteSpace(setupsJson))
        {
            var setups = JsonSerializer.Deserialize<List<ProjectSetupInfo>>(setupsJson, serializerOptions)!;
            foreach (var setup in setups)
            {
                var url = (AzureDevOpsProjectUrl)setup.Url;
                var project = projects.SingleOrDefault(p => new Uri(p.Url!) == setup.Url);
                if (project is null)
                {
                    project = new Models.Management.Project
                    {
                        Id = Guid.NewGuid().ToString("n"),
                        Created = DateTimeOffset.UtcNow,
                        Password = GeneratePassword(32),
                        Url = setup.Url.ToString(),
                        Type = Models.Management.ProjectType.Azure,
                    };
                    await context.Projects.AddAsync(project, cancellationToken);
                }

                project.Token = setup.Token;
                project.AutoComplete.Enabled = setup.AutoComplete;
                project.AutoComplete.IgnoreConfigs = setup.AutoCompleteIgnoreConfigs;
                project.AutoComplete.MergeStrategy = setup.AutoCompleteMergeStrategy;
                project.AutoApprove.Enabled = setup.AutoApprove;
                project.Secrets = setup.Secrets;
                var tp = await adoProvider.GetProjectAsync(project, cancellationToken);
                project.Name = tp.Name;
                project.ProviderId = tp.Id.ToString();
                if (context.ChangeTracker.HasChanges())
                {
                    project.Updated = DateTimeOffset.UtcNow;
                }
            }

            // update databases
            var updated = await context.SaveChangesAsync(cancellationToken);

            // update projects if we updated the db
            projects = updated > 0 ? await context.Projects.ToListAsync(cancellationToken) : projects;
        }

        var options = provider.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        var synchronizer = provider.GetRequiredService<Synchronizer>();
        foreach (var project in projects)
        {
            // synchronize project
            if (options.SynchronizeOnStartup)
            {
                await synchronizer.SynchronizeAsync(project, false, cancellationToken); /* database sync should not trigger, just in case it's too many */
            }

            // create or update webhooks/subscriptions
            if (options.CreateOrUpdateWebhooksOnStartup)
            {
                await adoProvider.CreateOrUpdateSubscriptionsAsync(project, cancellationToken);
            }
        }

        // skip loading schedules if told to
        if (!app.Configuration.GetValue<bool>("SKIP_LOAD_SCHEDULES"))
        {
            var repositories = await context.Repositories.ToListAsync(cancellationToken);
            var scheduler = provider.GetRequiredService<UpdateScheduler>();
            foreach (var repository in repositories)
            {
                await scheduler.CreateOrUpdateAsync(repository, cancellationToken);
            }
        }
    }

    private static string GeneratePassword(int length = 32)
    {
        var data = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
        return Convert.ToBase64String(data);
    }
}
