namespace SoftTime.Application.DTOs;

public record PayrollWriteResultDto(string Database, string TypeBase, int EmployeesUpdated, int CategoriesUpdated, string Message);
