
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
                var tokenEndpoint = Environment.GetEnvironmentVariable("COGNITO_URL") + "/oauth2/token";

                var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");

                var requestData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", redirectUri),
                });

                HttpResponseMessage response = await _httpClient.PostAsync(tokenEndpoint, requestData);

                // response.EnsureSuccessStatusCode();

                string stringResponse = await response.Content.ReadAsStringAsync();
                TokenResponse jsonResponse = JsonSerializer.Deserialize<TokenResponse>(stringResponse);
                return jsonResponse;

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
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
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

                    Console.WriteLine($"Attempt {i + 1} - Starting OAuth process with code: {id}");

                    var tokenResponse = await oauthClient.GetOAuthToken(clientId, clientSecret, id, redirectUri);
                    
                    if (tokenResponse?.access_token == null)
                    {
                        if (i < maxRetries - 1)
                        {
                            Console.WriteLine($"Token response was null, retrying... Attempt {i + 1}");
                            await Task.Delay(2000);
                            continue;
                        }
                        return BadRequest("Failed to get OAuth token after multiple attempts");
                    }

                    var userInfoResponse = await oauthClient.GetUserDetails(tokenResponse.access_token);
                    
                    if (userInfoResponse?.sub == null)
                    {
                        return BadRequest("Failed to get user info");
                    }

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
                                Console.WriteLine($"Failed to set session after 3 attempts: {ex.Message}");
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
                    if (i < maxRetries - 1)
                    {
                        Console.WriteLine($"Attempt {i + 1} failed: {ex.Message}");
                        await Task.Delay(2000);
                        continue;
                    }
                    Console.WriteLine($"All attempts failed. Final error: {ex.Message}");
                    return StatusCode(500, $"Internal server error after {maxRetries} attempts");
                }
            }

            return StatusCode(500, "Unexpected error occurred");
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
