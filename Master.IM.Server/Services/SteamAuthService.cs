using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.Server.Services;

/// <summary>
/// Steam认证服务
/// </summary>
public class SteamAuthService
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;

    public SteamAuthService(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    public async Task<SteamAuthResponse> ValidateTicketAsync(string ticket)
    {
        try
        {
            var url = $"https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key={_apiKey}&appid=YOUR_APP_ID&ticket={ticket}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            
            var json = JsonSerializer.Deserialize<JsonElement>(content);
            
            if (json.TryGetProperty("response", out var resp) && 
                resp.TryGetProperty("params", out var params_) &&
                params_.TryGetProperty("steamid", out var steamid))
            {
                return new SteamAuthResponse
                {
                    Success = true,
                    SteamId = steamid.GetString() ?? ""
                };
            }
            
            return new SteamAuthResponse
            {
                Success = false,
                ErrorMessage = "Invalid ticket"
            };
        }
        catch (Exception ex)
        {
            return new SteamAuthResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
