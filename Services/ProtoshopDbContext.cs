using fileshare.Entities;
using Microsoft.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore.Extensions;

namespace fileshare.Services
{
    public class ProtoshopDbContext : DbContext
    {

        public DbSet<TopLevelUserObject> TopLevelUserObjects { get; set; }

        public ProtoshopDbContext(DbContextOptions<ProtoshopDbContext> options) : base(options){

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // modelBuilder.Entity<TopLevelUserObject>(entity =>
            // {
            //     entity.HasKey(e => e.Id);
            //     entity.Property(e => e.UserId).IsRequired();
            //     entity.OwnsMany(u => u.Directory, d =>
            //         {
            //             d.OwnsMany(ud => ud.ObjChildren);
            //         });
            // });
        }
    }
}
