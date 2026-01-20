namespace Vlad3.Application.Errors;

public sealed class ServiceException : Exception
{
    public int StatusCode { get; }

    public ServiceException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ServiceException BadRequest(string message) => new(400, message);
    public static ServiceException Unauthorized(string message) => new(401, message);
    public static ServiceException Forbidden(string message) => new(403, message);
    public static ServiceException NotFound(string message) => new(404, message);
    public static ServiceException Conflict(string message) => new(409, message);
}
