using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace fileshare
{
    [Route("/api/[controller]")]
    [ApiController]

    public class SessionController : ControllerBase
    {

        public void SetSessionInfo(string SubId, string Username, string AccessToken, string RefreshToken){

            if(string.IsNullOrWhiteSpace(HttpContext.Session.GetString(SessionVariables.SessionSubId ))){
                HttpContext.Session.SetString(SessionVariables.SessionSubId, SubId);
                HttpContext.Session.SetString(SessionVariables.SessionUsername, Username);
                HttpContext.Session.SetString(SessionVariables.SessionAccessToken, AccessToken);
                HttpContext.Session.SetString(SessionVariables.SessionRefreshToken, RefreshToken);
            }
        }

        public IEnumerable<string> GetSessionInfo(){
            List<string> sessionInfo = new List<string>();
            var subId = HttpContext.Session.GetString(SessionVariables.SessionSubId);
            var username = HttpContext.Session.GetString(SessionVariables.SessionUsername);
            var accessToken = HttpContext.Session.GetString(SessionVariables.SessionAccessToken);
            var refreshToken = HttpContext.Session.GetString(SessionVariables.SessionRefreshToken);

            sessionInfo.Add(subId);
            sessionInfo.Add(username);
            sessionInfo.Add(accessToken);
            sessionInfo.Add(refreshToken);
        
            return sessionInfo;
        }
    }
}
