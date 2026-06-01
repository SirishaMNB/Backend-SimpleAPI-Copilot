
using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);


var key = "THIS_IS_A_VERY_SECRET_KEY_12345678"; // store in config ideally

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})

.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };

    //  Standardized unauthorized response
    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Unauthorized",
                statusCode = 401
            });
        }
    };
});


builder.Services.AddAuthorization();


var app = builder.Build();

//  In-memory data
var users = new ConcurrentDictionary<int, User>
(
    new[]
    {
        new KeyValuePair<int, User>(1, new User { Id = 1, Name = "John", Email = "john@example.com" }),
        new KeyValuePair<int, User>(2, new User { Id = 2, Name = "Jane", Email = "jane@example.com" })
    }
);


//  Add middleware here
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();


string GenerateToken(string username)
{
    var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username)
    };

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.Now.AddHours(1),
        signingCredentials: credentials
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}


app.MapGet("/", () => ApiResponse<string>.Ok("I'm root")).AllowAnonymous();


//  Login endpoint (GET TOKEN)
app.MapPost("/login", (User loginUser) =>
{
    if (loginUser.Name == "admin" && loginUser.Email == "admin@example.com")
    {
        var token = GenerateToken(loginUser.Name);

        return ApiResponse<string>.Ok(token);
    }

    return ApiResponse<string>.Fail("Invalid credentials", 401);

}).AllowAnonymous();




//  GET all users
app.MapGet("/users", () =>
{
    return ApiResponse<IEnumerable<User>>.Ok(users.Values);
}).RequireAuthorization();



//  GET user by ID
app.MapGet("/users/{id}", (int id) =>
{
    return users.TryGetValue(id, out var user)
        ? ApiResponse<User>.Ok(user)
        : ApiResponse<User>.Fail("User not found", 404);
}).RequireAuthorization();


//  CREATE user
app.MapPost("/users", (User user) =>
{
    
    if (string.IsNullOrWhiteSpace(user.Name))
        return ApiResponse<User>.Fail("Name is required", 400);

    user.Id = users.Count == 0 ? 1 : users.Max(u => u.Value.Id) + 1;
    users.TryAdd(user.Id, user);

    return Results.Created($"/users/{user.Id}", 
                            new ApiResponse<User>
                               {
                                   Success = true,
                                   Data = user,
                                   StatusCode = 201
                               });

}).RequireAuthorization();


//  UPDATE user
app.MapPut("/users/{id}", (int id, User updatedUser) =>
{
    // var user = users.FirstOrDefault(u => u.Value.Id == id);

    if (!users.TryGetValue(id, out var user))
        return ApiResponse<User>.Fail("User not found", 404);

    if (id != updatedUser.Id)
        return ApiResponse<User>.Fail("ID mismatch", 400);

    user.Name = updatedUser.Name;
    user.Email = updatedUser.Email;

    return ApiResponse<string>.Ok("User updated successfully");
}).RequireAuthorization();


//  DELETE user
app.MapDelete("/users/{id}", (int id) =>
{
    // var user = users.FirstOrDefault(u => u.Id == id);
    if (!users.TryRemove(id, out _))        
        return ApiResponse<string>.Fail("User not found", 404);

    return ApiResponse<string>.Ok("User deleted successfully");

}).RequireAuthorization();


app.Run();


// Model
record User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}



public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;


    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    // public RequestResponseLoggingMiddleware(RequestDelegate next)
    // {
    //     _next = next;
    // }

   
    public async Task Invoke(HttpContext context)
    {
        context.Request.EnableBuffering();

        var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        _logger.LogInformation("Request: {method} {path} {body}",
            context.Request.Method, context.Request.Path, requestBody);

        var originalBodyStream = context.Response.Body;
        var responseBody = new MemoryStream();

        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        finally
        {
            //  Always execute (even if exception occurs)

            responseBody.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(responseBody).ReadToEndAsync();

            _logger.LogInformation("Response: {status} {body}",
                context.Response.StatusCode, responseText);

            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);

            context.Response.Body = originalBodyStream;

            responseBody.Dispose(); //  manual cleanup AFTER use
        }
    }

}


public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public int StatusCode { get; set; }

    public static IResult Ok(T data) =>
        Results.Json(new ApiResponse<T>
        {
            Success = true,
            Data = data,
            StatusCode = StatusCodes.Status200OK
        }, statusCode: StatusCodes.Status200OK);

    public static IResult Fail(string error, int statusCode) =>
        Results.Json(new ApiResponse<T>
        {
            Success = false,
            Error = error,
            StatusCode = statusCode
        }, statusCode: statusCode);
}

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public GlobalExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var response = new ApiResponse<string>
            {
                Success = false,
                Error = "An unexpected error occurred." + ex.Message,
                StatusCode = 500
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}



