using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class UserPermissionsTests
{
    [Fact]
    public void UserDetailedPermissionsDto_Default_IsEmpty()
    {
        var dto = new UserDetailedPermissionsDto();
        dto.UserId.Should().Be(0);
        dto.GrantedPermissions.Should().BeEmpty();
        dto.IsCustomized.Should().BeFalse();
    }

    [Fact]
    public void UserDetailedPermissionsDto_Constructor_StoresPermissions()
    {
        var perms = new List<string> { "sage.1.read", "users.read" };
        var dto = new UserDetailedPermissionsDto(5, perms, true);

        dto.UserId.Should().Be(5);
        dto.GrantedPermissions.Count.Should().Be(2);
        dto.IsCustomized.Should().BeTrue();
        dto.GrantedPermissions.Should().Contain("sage.1.read");
        dto.GrantedPermissions.Should().Contain("users.read");
    }

    [Fact]
    public void T_USER_PERMISSION_InitializesCorrectly()
    {
        var entity = new UserPermission
        {
            Id = 1,
            IdResponsable = 10,
            PermissionCode = "pointeuse.1.read",
            IsGranted = true
        };

        entity.Id.Should().Be(1);
        entity.IdResponsable.Should().Be(10);
        entity.PermissionCode.Should().Be("pointeuse.1.read");
        entity.IsGranted.Should().BeTrue();
    }
}
