using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class UserPermissionsTests
{
    [Fact]
    public void UserDetailedPermissionsDto_Default_IsEmpty()
    {
        var dto = new UserDetailedPermissionsDto();
        Assert.Equal(0, dto.UserId);
        Assert.Empty(dto.GrantedPermissions);
        Assert.False(dto.IsCustomized);
    }

    [Fact]
    public void UserDetailedPermissionsDto_StoresPermissions()
    {
        var perms = new List<string> { "SAGE:DB1:READ", "PARAMS:USERS" };
        var dto = new UserDetailedPermissionsDto(5, perms, true);

        Assert.Equal(5, dto.UserId);
        Assert.Equal(2, dto.GrantedPermissions.Count);
        Assert.True(dto.IsCustomized);
        Assert.Contains("SAGE:DB1:READ", dto.GrantedPermissions);
        Assert.Contains("PARAMS:USERS", dto.GrantedPermissions);
    }

    [Fact]
    public void T_USER_PERMISSION_InitializesCorrectly()
    {
        var entity = new UserPermission
        {
            Id = 1,
            IdResponsable = 10,
            PermissionCode = "POINTEUSE:CLOCK1:READ",
            IsGranted = true
        };

        Assert.Equal(1, entity.Id);
        Assert.Equal(10, entity.IdResponsable);
        Assert.Equal("POINTEUSE:CLOCK1:READ", entity.PermissionCode);
        Assert.True(entity.IsGranted);
    }
}
