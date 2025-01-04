
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using fileshare.Services;
using fileshare.Entities;
using Microsoft.OpenApi.Any;
using System.Text.Json.Serialization;
using Amazon.Runtime.Internal.Transform;
using Microsoft.Extensions.Configuration;


namespace fileshare.Controllers
{
    [ApiController]

    public class ProtoshopController : ControllerBase
    {

        private readonly ProtoshopService _protoshopService;
        private readonly IConfiguration _configuration;

        public ProtoshopController(ProtoshopService protoshopService, IConfiguration configuration){
            _protoshopService = protoshopService;
             _configuration = configuration;   
        }

        public class UserInfoResponse {
            public string? sub { get; set; }
            public string? email_verified { get; set; }
            public string? email { get; set; }
            public string? username { get; set; }
        }
    
        public class TokenResponse
        {
            public string? id_token { get; set; }
            public string? access_token { get; set; }
            public string? refresh_token { get; set;}
            public int? expires_in { get; set; }
            public string? token_type { get; set;}
    
        }

        public class MoveItemRequest {
            public string? objId {get; set; }
            public string? newParentId {get; set;}
        }

        public class GetItemRequest {
            [JsonPropertyName("objId")]
            public string? objId {get; set; }
        }

        public class OAuthClient
        {
            private readonly HttpClient _httpClient;

            public OAuthClient(HttpClient httpClient)
            {
                _httpClient = httpClient;
            }

public async Task<TokenResponse> GetOAuthToken(string clientId, string clientSecret, string code, string redirectUri)
{
    Console.WriteLine($"Client ID starts with: {clientId?.Substring(0, Math.Min(4, clientId?.Length ?? 0))}");
    Console.WriteLine($"Redirect URI starts with: {redirectUri?.Substring(0, Math.Min(10, redirectUri?.Length ?? 0))}");
    Console.WriteLine($"Cognito URL starts with: {Environment.GetEnvironmentVariable("COGNITO_URL")?.Substring(0, Math.Min(10, Environment.GetEnvironmentVariable("COGNITO_URL")?.Length ?? 0))}");

    try 
    {

        var tokenEndpoint = Environment.GetEnvironmentVariable("COGNITO_URL") + "/oauth2/token";
        
        // Log values for debugging (mask secrets)
        Console.WriteLine($"Client ID length: {clientId?.Length ?? 0}");
        Console.WriteLine($"Client Secret length: {clientSecret?.Length ?? 0}");
        Console.WriteLine($"Token Endpoint: {tokenEndpoint}");
        Console.WriteLine($"Redirect URI: {redirectUri}");

        // Clear any existing headers
        _httpClient.DefaultRequestHeaders.Clear();

        // Create auth header
        var authBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        var authHeader = Convert.ToBase64String(authBytes);
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

        // Create form data
        var formData = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", redirectUri }
        };

        var content = new FormUrlEncodedContent(formData);

        // Log the request
        Console.WriteLine("Request Headers:");
        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            Console.WriteLine($"{header.Key}: {(header.Key.ToLower() == "authorization" ? "[MASKED]" : string.Join(", ", header.Value))}");
        }

        var response = await _httpClient.PostAsync(tokenEndpoint, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"Response Status: {response.StatusCode}");
        Console.WriteLine($"Response Body: {responseBody}");

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"OAuth Error: {responseBody}");
        }

        return JsonSerializer.Deserialize<TokenResponse>(responseBody);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"OAuth Error Details: {ex.Message}");
        throw;
    }
}
         public async Task<UserInfoResponse> GetUserDetails(string accessToken)
            {
                var tokenEndpoint = Environment.GetEnvironmentVariable("COGNITO_URL") + "/oauth2/userinfo";

                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                HttpResponseMessage response = await _httpClient.GetAsync(tokenEndpoint);

                // response.EnsureSuccessStatusCode();

                string stringResponse = await response.Content.ReadAsStringAsync();
                UserInfoResponse jsonResponse = JsonSerializer.Deserialize<UserInfoResponse>(stringResponse);
                return jsonResponse;

        }}

    // Mongo 
[Route("login/{id}")]
[HttpPost]
public async Task<IActionResult> GetLoginInfoAsync(string id)
{
    try 
    {
        var httpClient = new HttpClient();
        var oauthClient = new OAuthClient(httpClient);

        string? clientId = Environment.GetEnvironmentVariable("AUTH0_CLIENT_ID") 
            ?? _configuration["Auth0:ClientId"];
        string? clientSecret = Environment.GetEnvironmentVariable("AUTH0_CLIENT_SECRET") 
            ?? _configuration["Auth0:ClientSecret"];
        string redirectUri = Environment.GetEnvironmentVariable("REDIRECT_URL");

        // Single attempt for OAuth token since code can only be used once
        var tokenResponse = await oauthClient.GetOAuthToken(clientId, clientSecret, id, redirectUri);
            
        if (tokenResponse?.access_token == null)
        {
            return BadRequest("Failed to get OAuth token");
        }

        var userInfoResponse = await oauthClient.GetUserDetails(tokenResponse.access_token);
            
        if (userInfoResponse?.sub == null)
        {
            return BadRequest("Failed to get user info");
        }

        // We can keep retries for session handling since this doesn't involve the auth code
        for (int sessionTry = 0; sessionTry < 3; sessionTry++)
        {
            try
            {
                HttpContext.Session.SetString(SessionVariables.SessionSubId, userInfoResponse.sub);
                HttpContext.Session.SetString(SessionVariables.SessionUsername, userInfoResponse.username ?? "default");
                HttpContext.Session.SetString(SessionVariables.SessionAccessToken, tokenResponse.access_token);
                HttpContext.Session.SetString(SessionVariables.SessionRefreshToken, tokenResponse.refresh_token ?? "");
                break;
            }
            catch (Exception ex)
            {
                if (sessionTry == 2)
                {
                    Console.WriteLine($"Failed to set session: {ex.Message}");
                }
                else
                {
                    await Task.Delay(500);
                }
            }
        }

        var userObject = _protoshopService.GetUserObject(userInfoResponse.sub);

        if (userObject == null)
        {
            _protoshopService.CreateNewUserObject(userInfoResponse.sub, userInfoResponse.username);
            userObject = _protoshopService.GetUserObject(userInfoResponse.sub);
        }

        return Ok(userObject);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in login: {ex.Message}");
        return StatusCode(500, $"Internal server error: {ex.Message}");
    }
}

        [Route("UserObject")]
        [HttpGet]
        public TopLevelUserObject? GetUserObject()
        {
            var userObject = _protoshopService.GetUserObject(HttpContext.Session.GetString(SessionVariables.SessionSubId));

            return userObject;
        }

        [HttpPost]
        [Route("Delete/{objForDeletion}")]
        public  async Task<TopLevelUserObject> Delete(string objForDeletion){
            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            var userObject = await _protoshopService.DeleteItem(userId, objForDeletion);

            if (userObject){
                await _protoshopService.DeleteS3Object(userId, objForDeletion);
                return _protoshopService.GetUserObject(userId);
            } else {
                return null;
            }
        }

        [HttpPost]
        [Route("AddFile/{parentId}")]
        public  async Task<TopLevelUserObject> CreateFile([FromBody] UserDirectory obj, string parentId){
            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            string parentFolderObjId = parentId;
            string objId = Guid.NewGuid().ToString();

            var newFile = new UserDirectory
            {
                ObjId = obj.ObjId,
                Name = obj.Name,
                IsFolder = false,
            };

            var result = await _protoshopService.AddFile(userId, parentFolderObjId, newFile);

            if (result){
                return _protoshopService.GetUserObject(HttpContext.Session.GetString(SessionVariables.SessionSubId));
            } else {
                return null;
            }
        }

        [HttpPost]
        [Route("CreateFolder/{parentId}")]
        public  async Task<TopLevelUserObject> CreateFolder([FromBody] UserDirectory obj, string parentId){
            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            string parentFolderObjId = parentId;
            string objId = Guid.NewGuid().ToString();

            var newFile = new UserDirectory
            {
                ObjId = objId,
                Name = obj.Name,
                IsFolder = true,
            };

            var result = await _protoshopService.CreateFolder(userId, parentFolderObjId, newFile);

            if (result){
                return _protoshopService.GetUserObject(HttpContext.Session.GetString(SessionVariables.SessionSubId));
            } else {
                return null;
            }
        }

        [HttpPost]
        [Route("RenameItem/{parentId}")]
        public  async Task<TopLevelUserObject> RenameItem([FromBody] UserDirectory obj, string parentId){
            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            string parentFolderObjId = parentId;
            string newName = obj.Name;

            var result = await _protoshopService.RenameItem(userId, parentFolderObjId, newName);

            if (result){
                return _protoshopService.GetUserObject(HttpContext.Session.GetString(SessionVariables.SessionSubId));
            } else {
                return null;
            }
        }

        [HttpPost]
        [Route("MoveItem")]
        public  async Task<TopLevelUserObject> MoveItem([FromBody] MoveItemRequest obj){
            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            string objId = obj.objId;
            string newParentId = obj.newParentId;

            var result = await _protoshopService.MoveItem(userId, objId, newParentId);

            if (result){
                return _protoshopService.GetUserObject(HttpContext.Session.GetString(SessionVariables.SessionSubId));
            } else {
                return null;
            }
        }

        [HttpGet]
        [Route("SignOut")]
        public async void SignOut(){
            HttpContext.Session.Clear();
        }

        // S3 

        public class S3FormData {
            [JsonPropertyName("FileId")]
            public string? FileId { get; set; }
                
            [JsonPropertyName("FileContent")]
            public IFormFileCollection? FileContent { get; set; }
        }

        [HttpPost]
        [Route("GetFile")]
        public IActionResult GetS3File([FromBody] GetItemRequest obj) {
            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            string fileKey = $"{userId}/{obj.objId}"; 
            string signedUrl = _protoshopService.GenerateSignedUrl(fileKey);

            return Ok(new { url = signedUrl }); 
        }
            
        [HttpPost]
        [Route("GetFileContent")]
        public async Task<IActionResult> GetFileContent([FromBody] GetItemRequest obj)
        {
            try
            {
                string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
                string fileKey = $"{userId}/{obj.objId}";
                string signedUrl = _protoshopService.GenerateSignedUrl(fileKey);

                using (var client = new HttpClient())
                {
                    var fileContent = await client.GetAsync(signedUrl);
                    if (!fileContent.IsSuccessStatusCode)
                    {
                        return BadRequest("Failed to fetch file");
                    }

                    var contentType = fileContent.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                    var bytes = await fileContent.Content.ReadAsByteArrayAsync();
                    
                    return File(bytes, contentType);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [Consumes("multipart/form-data")]
        [HttpPost]
        [Route("AddS3File")]
        public async Task<IActionResult> AddS3File([FromForm] string FileId, [FromForm] IFormFile FileContent)
        {
            if (FileContent == null || FileContent.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            string userId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            string fileKey = $"{userId}/{FileId}";

            try
            {
                using (var stream = new MemoryStream())
                {
                    await FileContent.CopyToAsync(stream);
                    var fileContentBytes = stream.ToArray();
                    Console.WriteLine($"File Size: {fileContentBytes.Length} bytes");
                }

                await _protoshopService.UploadToS3(FileContent, fileKey); 
                return Ok("File uploaded successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error s3: {ex.Message}");
            }
        }
    }   
}
