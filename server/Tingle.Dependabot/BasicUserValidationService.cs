﻿using AspNetCore.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Tingle.Dependabot.Models;

namespace Tingle.Dependabot;

internal class BasicUserValidationService : IBasicUserValidationService
{
    private readonly MainDbContext dbContext;

    public BasicUserValidationService(MainDbContext dbContext)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<bool> IsValidAsync(string username, string password)
    {
        var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == username);
        return project is not null && string.Equals(project.Password, password, StringComparison.Ordinal);
    }
}
