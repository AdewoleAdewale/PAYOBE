using System;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace PAYYOBE.Services
{
    public static class OfficerSessionManager
    {
        private const string KeyId = "Off_Id";
        private const string KeyName = "Off_Name";
        private const string KeyCode = "Off_Code";
        private const string KeyEmail = "Off_Email";
        private const string KeyPhone = "Off_Phone";
        private const string KeyPass = "Off_Pass";
        private const string KeyTimestamp = "Off_LoginTime";

        // Session validation lifetime matching enterprise window (12 Hours)
        private static readonly TimeSpan SessionExpiryWindow = TimeSpan.FromHours(12);

        public static async Task SaveSessionAsync(int id, string name, string code, string email, string phone,string pass)
        {
            try
            {
                await SecureStorage.SetAsync(KeyId, id.ToString());
                await SecureStorage.SetAsync(KeyName, name ?? string.Empty);
                await SecureStorage.SetAsync(KeyCode, code ?? string.Empty);
                await SecureStorage.SetAsync(KeyEmail, email ?? string.Empty);
                await SecureStorage.SetAsync(KeyPhone, phone ?? string.Empty);
                await SecureStorage.SetAsync(KeyPhone, pass ?? string.Empty);
                await SecureStorage.SetAsync(KeyTimestamp, DateTime.UtcNow.ToBinary().ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionManager] Write Fault: {ex.Message}");
            }
        }

        public static async Task<bool> IsSessionValidAsync()
        {
            try
            {
                string rawId = await SecureStorage.GetAsync(KeyId);
                string rawTime = await SecureStorage.GetAsync(KeyTimestamp);

                if (string.IsNullOrEmpty(rawId) || string.IsNullOrEmpty(rawTime))
                    return false;

                if (!long.TryParse(rawTime, out long binaryTime))
                    return false;

                var loginDate = DateTime.FromBinary(binaryTime);
                if (DateTime.UtcNow - loginDate > SessionExpiryWindow)
                {
                    ClearSession();
                    return false;
                }

                // Hydrate global memory context for cross-page navigation security
                MainPage.OfficerId = int.Parse(rawId);
                MainPage.OfficerName = await SecureStorage.GetAsync(KeyName);
                MainPage.OfficerCode = await SecureStorage.GetAsync(KeyCode);
                MainPage.OfficerEmail = await SecureStorage.GetAsync(KeyEmail);
                MainPage.OfficerPhone = await SecureStorage.GetAsync(KeyPhone);
                MainPage.OfficerPassword = await SecureStorage.GetAsync(KeyPass);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ClearSession()
        {
            SecureStorage.Remove(KeyId);
            SecureStorage.Remove(KeyName);
            SecureStorage.Remove(KeyCode);
            SecureStorage.Remove(KeyEmail);
            SecureStorage.Remove(KeyPhone);
            SecureStorage.Remove(KeyTimestamp);

            MainPage.OfficerId = 0;
            MainPage.OfficerName = null;
            MainPage.OfficerCode = null;
            MainPage.OfficerEmail = null;
            MainPage.OfficerPhone = null;
            MainPage.OfficerPassword = null;
        }

        public static async Task CheckAndRouteSessionAsync()
        {
            if (await IsSessionValidAsync())
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    Application.Current.MainPage = new NavigationPage(new Views.Officer.Dashboard());
                });
            }
        }
    }
}