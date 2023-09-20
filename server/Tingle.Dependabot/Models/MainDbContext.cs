﻿using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tingle.Dependabot.Models.Management;

namespace Tingle.Dependabot.Models;

public class MainDbContext : DbContext, IDataProtectionKeyContext
{
    public MainDbContext(DbContextOptions<MainDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<UpdateJob> UpdateJobs => Set<UpdateJob>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Project>(builder =>
        {
            builder.OwnsOne(p => p.AutoApprove);
            builder.OwnsOne(p => p.AutoComplete, ownedBuilder =>
            {
                ownedBuilder.Property(ac => ac.IgnoreConfigs).HasJsonConversion();
            });
            builder.Property(p => p.Secrets).HasJsonConversion();

            builder.HasIndex(p => p.Created).IsDescending(); // faster filtering
            builder.HasIndex(p => p.ProviderId).IsUnique();
            builder.HasIndex(p => p.Password).IsUnique(); // password should be unique per project
        });

        modelBuilder.Entity<Repository>(builder =>
        {
            builder.Property(r => r.Updates).HasJsonConversion();
            builder.Property(r => r.Registries).HasJsonConversion();

            builder.HasIndex(r => r.Created).IsDescending(); // faster filtering
            builder.HasIndex(r => r.ProviderId).IsUnique();
        });

        modelBuilder.Entity<UpdateJob>(builder =>
        {
            builder.Property(j => j.PackageEcosystem).IsRequired();
            builder.OwnsOne(j => j.Error, ownedBuilder =>
            {
                ownedBuilder.Property(e => e.Detail).HasJsonConversion();
                ownedBuilder.HasIndex(e => e.Type); // faster filtering
            });

            builder.HasIndex(j => j.Created).IsDescending(); // faster filtering
            builder.HasIndex(j => j.RepositoryId);
            builder.HasIndex(j => new { j.PackageEcosystem, j.Directory, }); // faster filtering
            builder.HasIndex(j => new { j.PackageEcosystem, j.Directory, j.EventBusId, }).IsUnique();
            builder.HasIndex(j => j.AuthKey).IsUnique();

            builder.OwnsOne(j => j.Resources);
        });
    }
}
