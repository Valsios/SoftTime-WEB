using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Pointeuse;

namespace SoftTime.Infrastructure.Persistence.Configurations.PointeuseDb;

public sealed class CHECKINOUTConfiguration : IEntityTypeConfiguration<CHECKINOUT>
{
    public void Configure(EntityTypeBuilder<CHECKINOUT> builder)
    {
        builder.ToTable("CHECKINOUT");
        builder.HasKey(x => new { x.USERID, x.CHECKTIME });

        builder.Property(x => x.USERID)
            .HasColumnName("USERID")
            .ValueGeneratedNever();

        builder.Property(x => x.CHECKTIME)
            .HasColumnName("CHECKTIME")
            .ValueGeneratedNever();

        builder.Property(x => x.CHECKTYPE)
            .HasColumnName("CHECKTYPE")
            .IsRequired(false);

        builder.Property(x => x.Memoinfo)
            .HasColumnName("Memoinfo")
            .IsRequired(false);

        builder.Property(x => x.SENSORID)
            .HasColumnName("SENSORID")
            .IsRequired(false);

        builder.Property(x => x.UserExtFmt)
            .HasColumnName("UserExtFmt")
            .IsRequired(false);

        builder.Property(x => x.VERIFYCODE)
            .HasColumnName("VERIFYCODE")
            .IsRequired(false);

        builder.Property(x => x.WorkCode)
            .HasColumnName("WorkCode")
            .IsRequired(false);

        builder.Property(x => x.sn)
            .HasColumnName("sn")
            .IsRequired(false);
    }
}
