using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Pointeuse;

namespace SoftTime.Infrastructure.Persistence.Configurations.PointeuseDb;

public sealed class UserInfoConfiguration : IEntityTypeConfiguration<UserInfo>
{
    public void Configure(EntityTypeBuilder<UserInfo> builder)
    {
        builder.ToTable("USERINFO");
        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .HasColumnName("USERID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Att)
            .HasColumnName("ATT");

        builder.Property(x => x.AccGroup)
            .HasColumnName("AccGroup")
            .IsRequired(false);

        builder.Property(x => x.AutoSchPlan)
            .HasColumnName("AutoSchPlan")
            .IsRequired(false);

        builder.Property(x => x.BadgeNumber)
            .HasColumnName("BADGENUMBER")
            .IsRequired(false);

        builder.Property(x => x.Birthday)
            .HasColumnName("BIRTHDAY")
            .IsRequired(false);

        builder.Property(x => x.City)
            .HasColumnName("CITY")
            .IsRequired(false);

        builder.Property(x => x.CardNo)
            .HasColumnName("CardNo")
            .IsRequired(false);

        builder.Property(x => x.DefaultDeptId)
            .HasColumnName("DEFAULTDEPTID")
            .IsRequired(false);

        builder.Property(x => x.EmPrivilege)
            .HasColumnName("EMPRIVILEGE")
            .IsRequired(false);

        builder.Property(x => x.Expires)
            .HasColumnName("Expires")
            .IsRequired(false);

        builder.Property(x => x.FPhone)
            .HasColumnName("FPHONE")
            .IsRequired(false);

        builder.Property(x => x.FaceGroup)
            .HasColumnName("FaceGroup")
            .IsRequired(false);

        builder.Property(x => x.Gender)
            .HasColumnName("GENDER")
            .IsRequired(false);

        builder.Property(x => x.HiredDay)
            .HasColumnName("HIREDDAY")
            .IsRequired(false);

        builder.Property(x => x.Holiday)
            .HasColumnName("HOLIDAY");

        builder.Property(x => x.InLate)
            .HasColumnName("INLATE");

        builder.Property(x => x.InheritDeptRule)
            .HasColumnName("InheritDeptRule")
            .IsRequired(false);

        builder.Property(x => x.InheritDeptSch)
            .HasColumnName("InheritDeptSch")
            .IsRequired(false);

        builder.Property(x => x.InheritDeptSchClass)
            .HasColumnName("InheritDeptSchClass")
            .IsRequired(false);

        builder.Property(x => x.LunchDuration)
            .HasColumnName("LUNCHDURATION");

        builder.Property(x => x.Minzu)
            .HasColumnName("MINZU")
            .IsRequired(false);

        builder.Property(x => x.MVerifyPass)
            .HasColumnName("MVerifyPass")
            .IsRequired(false);

        builder.Property(x => x.MinAutoSchInterval)
            .HasColumnName("MinAutoSchInterval")
            .IsRequired(false);

        builder.Property(x => x.Name)
            .HasColumnName("NAME")
            .IsRequired(false);

        builder.Property(x => x.OPhone)
            .HasColumnName("OPHONE")
            .IsRequired(false);

        builder.Property(x => x.OutEarly)
            .HasColumnName("OUTEARLY");

        builder.Property(x => x.Overtime)
            .HasColumnName("OVERTIME");

        builder.Property(x => x.Pager)
            .HasColumnName("PAGER")
            .IsRequired(false);

        builder.Property(x => x.Password)
            .HasColumnName("PASSWORD")
            .IsRequired(false);

        builder.Property(x => x.RegisterOT)
            .HasColumnName("RegisterOT")
            .IsRequired(false);

        builder.Property(x => x.SecurityFlags)
            .HasColumnName("SECURITYFLAGS")
            .IsRequired(false);

        builder.Property(x => x.Sep)
            .HasColumnName("SEP");

        builder.Property(x => x.Ssn)
            .HasColumnName("SSN")
            .IsRequired(false);

        builder.Property(x => x.State)
            .HasColumnName("STATE")
            .IsRequired(false);

        builder.Property(x => x.Street)
            .HasColumnName("STREET")
            .IsRequired(false);

        builder.Property(x => x.Title)
            .HasColumnName("TITLE")
            .IsRequired(false);

        builder.Property(x => x.TimeZone1)
            .HasColumnName("TimeZone1")
            .IsRequired(false);

        builder.Property(x => x.TimeZone2)
            .HasColumnName("TimeZone2")
            .IsRequired(false);

        builder.Property(x => x.TimeZone3)
            .HasColumnName("TimeZone3")
            .IsRequired(false);

        builder.Property(x => x.UseAccGroupTZ)
            .HasColumnName("UseAccGroupTZ")
            .IsRequired(false);

        builder.Property(x => x.VerificationMethod)
            .HasColumnName("VERIFICATIONMETHOD")
            .IsRequired(false);

        builder.Property(x => x.ValidCount)
            .HasColumnName("ValidCount")
            .IsRequired(false);

        builder.Property(x => x.ValidTimeBegin)
            .HasColumnName("ValidTimeBegin")
            .IsRequired(false);

        builder.Property(x => x.ValidTimeEnd)
            .HasColumnName("ValidTimeEnd")
            .IsRequired(false);

        builder.Property(x => x.VerifyCode)
            .HasColumnName("VerifyCode")
            .IsRequired(false);

        builder.Property(x => x.Zip)
            .HasColumnName("ZIP")
            .IsRequired(false);

        builder.Property(x => x.Privilege)
            .HasColumnName("privilege")
            .IsRequired(false);
    }
}
