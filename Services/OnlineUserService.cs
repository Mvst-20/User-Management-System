namespace UserManagementSystem.Services;

public interface IOnlineUserService
{
    void MarkUserOnline(string userId);
    void MarkUserOffline(string userId);
    int GetOnlineUsersCount();
}

public class OnlineUserService : IOnlineUserService
{
    private readonly Dictionary<string, DateTime> _onlineUsers = new();
    private readonly object _lock = new();
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(30);

    public void MarkUserOnline(string userId)
    {
        lock (_lock)
        {
            _onlineUsers[userId] = DateTime.UtcNow;
        }
    }

    public void MarkUserOffline(string userId)
    {
        lock (_lock)
        {
            _onlineUsers.Remove(userId);
        }
    }

    public int GetOnlineUsersCount()
    {
        lock (_lock)
        {
            return _onlineUsers.Count;
        }
    }

    public void CleanExpiredUsers(TimeSpan? timeout = null)
    {
        var expiryTime = DateTime.UtcNow - (timeout ?? _defaultTimeout);
        lock (_lock)
        {
            var expiredKeys = _onlineUsers
                .Where(kvp => kvp.Value < expiryTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _onlineUsers.Remove(key);
            }
        }
    }
}
