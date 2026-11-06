using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Pointeuse;

namespace SoftTime.Infrastructure.Persistence.Configurations.PointeuseDb;

public sealed class USERINFOConfiguration : IEntityTypeConfiguration<USERINFO>
{
    public void Configure(EntityTypeBuilder<USERINFO> builder)
    {
        builder.ToTable("USERINFO");
        builder.HasKey(x => x.USERID);

        builder.Property(x => x.USERID)
            .HasColumnName("USERID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ATT)
            .HasColumnName("ATT");

        builder.Property(x => x.AccGroup)
            .HasColumnName("AccGroup")
            .IsRequired(false);

        builder.Property(x => x.AutoSchPlan)
            .HasColumnName("AutoSchPlan")
            .IsRequired(false);

        builder.Property(x => x.BADGENUMBER)
            .HasColumnName("BADGENUMBER")
            .IsRequired(true);

        builder.Property(x => x.BIRTHDAY)
            .HasColumnName("BIRTHDAY")
            .IsRequired(false);

        builder.Property(x => x.CITY)
            .HasColumnName("CITY")
            .IsRequired(false);

        builder.Property(x => x.CardNo)
            .HasColumnName("CardNo")
            .IsRequired(false);

        builder.Property(x => x.DEFAULTDEPTID)
            .HasColumnName("DEFAULTDEPTID")
            .IsRequired(false);

        builder.Property(x => x.EMPRIVILEGE)
            .HasColumnName("EMPRIVILEGE")
            .IsRequired(false);

        builder.Property(x => x.Expires)
            .HasColumnName("Expires")
            .IsRequired(false);

        builder.Property(x => x.FPHONE)
            .HasColumnName("FPHONE")
            .IsRequired(false);

        builder.Property(x => x.FaceGroup)
            .HasColumnName("FaceGroup")
            .IsRequired(false);

        builder.Property(x => x.GENDER)
            .HasColumnName("GENDER")
            .IsRequired(false);

        builder.Property(x => x.HIREDDAY)
            .HasColumnName("HIREDDAY")
            .IsRequired(false);

        builder.Property(x => x.HOLIDAY)
            .HasColumnName("HOLIDAY");

        builder.Property(x => x.INLATE)
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

        builder.Property(x => x.LUNCHDURATION)
            .HasColumnName("LUNCHDURATION");

        builder.Property(x => x.MINZU)
            .HasColumnName("MINZU")
            .IsRequired(false);

        builder.Property(x => x.MVerifyPass)
            .HasColumnName("MVerifyPass")
            .IsRequired(false);

        builder.Property(x => x.MinAutoSchInterval)
            .HasColumnName("MinAutoSchInterval")
            .IsRequired(false);

        builder.Property(x => x.NAME)
            .HasColumnName("NAME")
            .IsRequired(false);

        builder.Property(x => x.OPHONE)
            .HasColumnName("OPHONE")
            .IsRequired(false);

        builder.Property(x => x.OUTEARLY)
            .HasColumnName("OUTEARLY");

        builder.Property(x => x.OVERTIME)
            .HasColumnName("OVERTIME");

        builder.Property(x => x.PAGER)
            .HasColumnName("PAGER")
            .IsRequired(false);

        builder.Property(x => x.PASSWORD)
            .HasColumnName("PASSWORD")
            .IsRequired(false);

        builder.Property(x => x.RegisterOT)
            .HasColumnName("RegisterOT")
            .IsRequired(false);

        builder.Property(x => x.SECURITYFLAGS)
            .HasColumnName("SECURITYFLAGS")
            .IsRequired(false);

        builder.Property(x => x.SEP)
            .HasColumnName("SEP");

        builder.Property(x => x.SSN)
            .HasColumnName("SSN")
            .IsRequired(false);

        builder.Property(x => x.STATE)
            .HasColumnName("STATE")
            .IsRequired(false);

        builder.Property(x => x.STREET)
            .HasColumnName("STREET")
            .IsRequired(false);

        builder.Property(x => x.TITLE)
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

        builder.Property(x => x.VERIFICATIONMETHOD)
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

        builder.Property(x => x.ZIP)
            .HasColumnName("ZIP")
            .IsRequired(false);

        builder.Property(x => x.privilege)
            .HasColumnName("privilege")
            .IsRequired(false);
    }
}
