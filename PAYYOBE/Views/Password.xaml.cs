using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace PAYYOBE.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Password : ContentPage
    {
        public Password()
        {
            InitializeComponent();
        }

        // ── Back / close tap ─────────────────────────────────────────────
        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            // If pushed via Navigation.PushAsync pop normally;
            // if pushed modally, pop modal.
            try
            {
                if (Navigation.NavigationStack.Count > 1)
                    await Navigation.PopAsync();
                else
                    await Navigation.PopModalAsync();
            }
            catch { }
        }

        // ── Submit tap ───────────────────────────────────────────────────
        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            string current = OldPasswordEntry.Text?.Trim();
            string newPass = ConfirmPassword.Text?.Trim();

            // ── Validation ───────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(current))
            {
                await DisplayAlert("Missing Field", "Please enter your current password.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(newPass) || newPass.Length < 6)
            {
                await DisplayAlert("Weak Password",
                    "New password must be at least 6 characters long.", "OK");
                return;
            }

            if (!Regex.IsMatch(newPass, @"[A-Z]"))
            {
                await DisplayAlert("Weak Password",
                    "New password must contain at least one uppercase letter.", "OK");
                return;
            }

            if (!Regex.IsMatch(newPass, @"[!@#$%^&*()\-_=+,.?"":{}|<>]"))
            {
                await DisplayAlert("Weak Password",
                    "New password must contain at least one special character.", "OK");
                return;
            }

            using (UserDialogs.Instance.Loading("Updating password…"))
            {
                await ChangePasswordAsync(current, newPass);
            }
        }

        // ── API call ─────────────────────────────────────────────────────
        private async Task ChangePasswordAsync(string currentPassword, string newPassword)
        {
            try
            {
                var payload = new
                {
                    Email = MainPage.myemail,
                    CurrentPassword = currentPassword,
                    NewPassword = newPassword,
                    ConfirmNewPassword = newPassword
                };

                string jsonBody = JsonConvert.SerializeObject(payload);

                using (var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (a, b, c, d) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                   | System.Security.Authentication.SslProtocols.Tls11
                })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                {
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(
                        "https://payyobe.com/api/v1/change-password", content);

                    string json = await response.Content.ReadAsStringAsync();

                    PasswordResponse result = null;
                    try { result = JsonConvert.DeserializeObject<PasswordResponse>(json); } catch { }

                    await Device.InvokeOnMainThreadAsync(async () =>
                    {
                        bool success = result?.status == true || response.IsSuccessStatusCode;

                        if (success)
                        {
                            // Mark so the first-login prompt never appears again for this user
                            Dashboard.MarkPasswordChanged(MainPage.myemail);

                            await DisplayAlert("✅ Password Updated",
                                result?.message ?? "Password changed successfully. Please log in again.",
                                "OK");

                            // ── Auto-logout ────────────────────────────────
                            MainPage.MyfullName = null;
                            MainPage.myemail = null;
                            MainPage.mymda = null;
                            MainPage.mycourt = null;
                            MainPage.superagents = null;

                            Application.Current.MainPage = new NavigationPage(new MainPage());
                        }
                        else
                        {
                            string msg = result?.message;
                            if (string.IsNullOrWhiteSpace(msg) && !response.IsSuccessStatusCode)
                                msg = $"Server error {(int)response.StatusCode}. Please try again.";
                            msg = msg ?? "Password could not be changed. Please try again.";
                            await DisplayAlert("Update Failed", msg, "OK");
                        }
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                await Device.InvokeOnMainThreadAsync(() =>
                    DisplayAlert("Connection Error",
                        $"Unable to update password. Check your internet connection.\n\n{ex.Message}",
                        "Retry"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangePassword] {ex}");
                await Device.InvokeOnMainThreadAsync(() =>
                    DisplayAlert("Error", "An unexpected error occurred. Please try again.", "OK"));
            }
        }

        // ── Response model ───────────────────────────────────────────────
        internal class PasswordResponse
        {
            [JsonProperty("status")] public bool status { get; set; }
            [JsonProperty("message")] public string message { get; set; }
        }
    }
}