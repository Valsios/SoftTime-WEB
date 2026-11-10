using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Pointeuse;

namespace SoftTime.Infrastructure.Persistence.Configurations.PointeuseDb;

public sealed class CheckInOutConfiguration : IEntityTypeConfiguration<CheckInOut>
{
    public void Configure(EntityTypeBuilder<CheckInOut> builder)
    {
        builder.ToTable("CHECKINOUT");
        builder.HasKey(x => new { x.UserId, x.CheckTime });
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<CheckInOut> builder)
    {
        builder.Property(x => x.CheckType).HasColumnName("CHECKTYPE").IsRequired(false);
        builder.Property(x => x.Memoinfo).HasColumnName("Memoinfo").IsRequired(false);
        builder.Property(x => x.SensorId).HasColumnName("SENSORID").IsRequired(false);
        builder.Property(x => x.Sn).HasColumnName("sn").IsRequired(false);
        builder.Property(x => x.WorkCode).HasColumnName("WorkCode").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<CheckInOut> builder)
    {
        builder.Property(x => x.CheckTime).HasColumnName("CHECKTIME").ValueGeneratedNever();
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<CheckInOut> builder)
    {
        builder.Property(x => x.UserId).HasColumnName("USERID").ValueGeneratedNever();
        builder.Property(x => x.UserExtFmt).HasColumnName("UserExtFmt").IsRequired(false);
        builder.Property(x => x.VerifyCode).HasColumnName("VERIFYCODE").IsRequired(false);
    }
}
