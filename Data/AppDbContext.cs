using Microsoft.EntityFrameworkCore;
using QuinielaApp.Models;

namespace QuinielaApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> o) : base(o) { }

        public DbSet<User>            Users           => Set<User>();
        public DbSet<Tournament>      Tournaments     => Set<Tournament>();
        public DbSet<Stage>           Stages          => Set<Stage>();
        public DbSet<Match>           Matches         => Set<Match>();
        public DbSet<TournamentEntry> TournamentEntries => Set<TournamentEntry>();
        public DbSet<StageEntry>      StageEntries    => Set<StageEntry>();
        public DbSet<Payment>         Payments        => Set<Payment>();
        public DbSet<Prediction>      Predictions     => Set<Prediction>();
        public DbSet<StageResult>     StageResults    => Set<StageResult>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<User>(e => {
                e.HasIndex(u => u.Email).IsUnique();
                e.HasIndex(u => u.Username).IsUnique();
            });

            mb.Entity<Tournament>(e => {
                e.Property(t => t.Status).HasConversion<string>();
                e.Property(t => t.Type).HasConversion<string>();
            });

            mb.Entity<Stage>(e => {
                e.Property(s => s.EntryFee).HasColumnType("decimal(10,2)");
                e.Property(s => s.Status).HasConversion<string>();
                e.Property(s => s.Type).HasConversion<string>();
                e.HasOne(s => s.Tournament).WithMany(t => t.Stages)
                 .HasForeignKey(s => s.TournamentId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<Match>(e => {
                e.HasIndex(m => m.ApiMatchId).IsUnique();
                e.HasOne(m => m.Stage).WithMany(s => s.Matches)
                 .HasForeignKey(m => m.StageId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<TournamentEntry>(e => {
                e.HasIndex(te => new { te.UserId, te.TournamentId }).IsUnique();
                e.HasOne(te => te.User).WithMany(u => u.Entries)
                 .HasForeignKey(te => te.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(te => te.Tournament).WithMany(t => t.Entries)
                 .HasForeignKey(te => te.TournamentId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<StageEntry>(e => {
                e.HasIndex(se => new { se.UserId, se.StageId }).IsUnique();
                e.HasOne(se => se.User).WithMany()
                 .HasForeignKey(se => se.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(se => se.Stage).WithMany(s => s.Entries)
                 .HasForeignKey(se => se.StageId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<Payment>(e => {
                e.Property(p => p.Amount).HasColumnType("decimal(10,2)");
                e.Property(p => p.Status).HasConversion<string>();
                e.HasOne(p => p.User).WithMany(u => u.Payments)
                 .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(p => p.Stage).WithMany()
                 .HasForeignKey(p => p.StageId).OnDelete(DeleteBehavior.Restrict);
            });

            mb.Entity<Prediction>(e => {
                e.HasIndex(p => new { p.UserId, p.MatchId }).IsUnique();
                e.Property(p => p.ResultPrediction).HasConversion<string>();
                e.HasOne(p => p.User).WithMany(u => u.Predictions)
                 .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(p => p.Match).WithMany(m => m.Predictions)
                 .HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<StageResult>(e => {
                e.HasIndex(sr => new { sr.StageId, sr.UserId }).IsUnique();
                e.HasOne(sr => sr.Stage).WithMany(s => s.Results)
                 .HasForeignKey(sr => sr.StageId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(sr => sr.User).WithMany()
                 .HasForeignKey(sr => sr.UserId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
