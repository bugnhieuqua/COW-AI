using Microsoft.EntityFrameworkCore;
using COW_AI.Models;

namespace COW_AI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<WorkspaceSession> WorkspaceSessions => Set<WorkspaceSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasMany(p => p.Scenes)
                  .WithOne(s => s.Project)
                  .HasForeignKey(s => s.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
