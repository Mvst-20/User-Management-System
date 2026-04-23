using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public interface IJwtService
{
    string GenerateToken(User user);
}
