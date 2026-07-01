using Acr.UserDialogs;
using Newtonsoft.Json;
using PAYYOBE.Services;
using PAYYOBE.Views;
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace PAYYOBE
{
    public partial class MainPage : ContentPage
    {
        // Shared State - Payobe User
        public static string MyfullName { get; set; }
        public static string mymda { get; set; }
        public static string myemail { get; set; }
        public static string mycourt { get; set; }
        public static string superagents { get; set; }

        // Shared State - Payobe Officer
        public static int OfficerId { get; set; }
        public static string OfficerName { get; set; }
        public static string OfficerCode { get; set; }
        public static string OfficerEmail { get; set; }
        public static string OfficerPhone { get; set; }
        public static string OfficerPassword { get; set; }

        private bool _isLoading = false;
        private bool _passwordVisible = false;
        private CancellationTokenSource _cancellationTokenSource;

        private static readonly Color ColDefault = Color.FromHex("#162A48");
        private static readonly Color ColFocused = Color.FromHex("#C8941A");
        private static readonly Color ColValid = Color.FromHex("#1A6B8A");
        private static readonly Color ColError = Color.FromHex("#D95F5F");

        public MainPage()
        {
            InitializeComponent();
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            if (await OfficerSessionManager.IsSessionValidAsync())
            {
                Application.Current.MainPage = new NavigationPage(new Views.Officer.Dashboard());
                return;
            }
            StartEntranceAnimations();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cancellationTokenSource?.Cancel();
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isLoading)
            {
                _cancellationTokenSource?.Cancel();
                return true;
            }
            return base.OnBackButtonPressed();
        }

        private async void StartEntranceAnimations()
        {
            try
            {
                HeaderSection.Opacity = 0; HeaderSection.TranslationY = -20;
                FormContainer.Opacity = 0; FormContainer.TranslationY = 70;
                FooterSection.Opacity = 0; FooterSection.TranslationY = 18;

                _ = AnimateOrbs();
                await Task.Delay(120);
                await Task.WhenAll(HeaderSection.FadeTo(1, 650, Easing.CubicOut), HeaderSection.TranslateTo(0, 0, 650, Easing.CubicOut));
                await SealFrame.ScaleTo(1.07, 220, Easing.SinIn);
                await SealFrame.ScaleTo(1.00, 220, Easing.SinOut);
                await Task.Delay(80);
                await Task.WhenAll(FormContainer.FadeTo(1, 580, Easing.CubicOut), FormContainer.TranslateTo(0, 0, 580, Easing.CubicOut));
                await AnimateFormElements();
                await Task.Delay(80);
                await Task.WhenAll(FooterSection.FadeTo(1, 480, Easing.CubicOut), FooterSection.TranslateTo(0, 0, 480, Easing.CubicOut));
            }
            catch { }
        }

        private async Task AnimateOrbs()
        {
            try { await Task.WhenAll(OrbTopRight.RotateTo(360, 36000, Easing.Linear), OrbBottomLeft.RotateTo(-360, 48000, Easing.Linear)); } catch { }
        }

        private async Task AnimateFormElements()
        {
            try
            {
                EmailSection.Opacity = 0; EmailSection.TranslationX = -36;
                PasswordSection.Opacity = 0; PasswordSection.TranslationX = 36;
                SignInButton.Opacity = 0; SignInButton.Scale = 0.90;
                await Task.WhenAll(EmailSection.FadeTo(1, 360, Easing.CubicOut), EmailSection.TranslateTo(0, 0, 360, Easing.CubicOut));
                await Task.Delay(110);
                await Task.WhenAll(PasswordSection.FadeTo(1, 360, Easing.CubicOut), PasswordSection.TranslateTo(0, 0, 360, Easing.CubicOut));
                await Task.Delay(110);
                await Task.WhenAll(SignInButton.FadeTo(1, 360, Easing.CubicOut), SignInButton.ScaleTo(1, 360, Easing.BounceOut));
            }
            catch { }
        }

        private async void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            try
            {
                _passwordVisible = !_passwordVisible;
                Password.IsPassword = !_passwordVisible;
                PasswordToggleBtn.Source = _passwordVisible ? "Openeyes" : "icons8eyes";
                await PasswordToggleBtn.ScaleTo(0.72, 75, Easing.CubicIn);
                await PasswordToggleBtn.ScaleTo(1.00, 75, Easing.CubicOut);
                Password.Focus();
            }
            catch { }
        }

        private async void OnEmailFocused(object sender, FocusEventArgs e)
        {
            EmailContainer.BorderColor = ColFocused; EmailContainer.BorderThickness = 2;
            await EmailContainer.ScaleTo(1.012, 120, Easing.CubicOut);
        }

        private async void OnEmailUnfocused(object sender, FocusEventArgs e)
        {
            await EmailContainer.ScaleTo(1.0, 120, Easing.CubicOut);
            EmailContainer.BorderColor = string.IsNullOrWhiteSpace(Email.Text) ? ColDefault : IsValidEmail(Email.Text) ? ColValid : ColError;
            EmailContainer.BorderThickness = 1;
        }

        private async void OnPasswordFocused(object sender, FocusEventArgs e)
        {
            PasswordContainer.BorderColor = ColFocused; PasswordContainer.BorderThickness = 2;
            await PasswordContainer.ScaleTo(1.012, 120, Easing.CubicOut);
        }

        private async void OnPasswordUnfocused(object sender, FocusEventArgs e)
        {
            await PasswordContainer.ScaleTo(1.0, 120, Easing.CubicOut);
            PasswordContainer.BorderColor = string.IsNullOrWhiteSpace(Password.Text) ? ColDefault : IsValidPassword(Password.Text) ? ColValid : ColError;
            PasswordContainer.BorderThickness = 1;
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$").IsMatch(email);
        }

        private static bool IsValidPassword(string password) => !string.IsNullOrWhiteSpace(password) && password.Length >= 6;

        private async Task<bool> ValidateInputs()
        {
            bool isValid = true;
            if (string.IsNullOrWhiteSpace(Email.Text))
            {
                SetFieldError(EmailContainer, EmailErrorLabel, "Staff ID or email address is required");
                await ShakeAsync(EmailContainer); isValid = false;
            }
            else if (!IsValidEmail(Email.Text))
            {
                SetFieldError(EmailContainer, EmailErrorLabel, "Please enter a valid staff email address");
                await ShakeAsync(EmailContainer); isValid = false;
            }
            else { SetFieldValid(EmailContainer, EmailErrorLabel); }

            if (string.IsNullOrWhiteSpace(Password.Text))
            {
                SetFieldError(PasswordContainer, PasswordErrorLabel, "Access passcode is required");
                await ShakeAsync(PasswordContainer); isValid = false;
            }
            else if (!IsValidPassword(Password.Text))
            {
                SetFieldError(PasswordContainer, PasswordErrorLabel, "Passcode must be at least 6 characters");
                await ShakeAsync(PasswordContainer); isValid = false;
            }
            else { SetFieldValid(PasswordContainer, PasswordErrorLabel); }
            return isValid;
        }

        private void SetFieldError(Xamarin.Forms.PancakeView.PancakeView container, Label errorLabel, string message)
        {
            container.BorderColor = ColError; container.BorderThickness = 2;
            errorLabel.Text = message; errorLabel.IsVisible = true;
        }

        private void SetFieldValid(Xamarin.Forms.PancakeView.PancakeView container, Label errorLabel)
        {
            container.BorderColor = ColValid; container.BorderThickness = 1;
            errorLabel.IsVisible = false;
        }

        private static async Task ShakeAsync(Xamarin.Forms.View element)
        {
            await element.TranslateTo(-8, 0, 40); await element.TranslateTo(8, 0, 40);
            await element.TranslateTo(-5, 0, 36); await element.TranslateTo(5, 0, 36);
            await element.TranslateTo(0, 0, 32);
        }

        private async Task SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            LoadingIndicator.IsVisible = isLoading; LoadingIndicator.IsRunning = isLoading;
            SignInLabel.Text = isLoading ? "AUTHENTICATING..." : "ACCESS TAX PORTAL";
            SignInButton.IsEnabled = !isLoading; Email.IsEnabled = !isLoading;
            Password.IsEnabled = !isLoading; PasswordToggleBtn.IsEnabled = !isLoading;
            await SignInButton.FadeTo(isLoading ? 0.65 : 1.00, 140);
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            if (_isLoading) return;
            try
            {
                await SignInButton.ScaleTo(0.97, 85, Easing.CubicIn); await SignInButton.ScaleTo(1.00, 85, Easing.CubicOut);
                if (!await ValidateInputs()) return;
                await SetLoadingState(true);
                _cancellationTokenSource = new CancellationTokenSource();
                await PerformLogin(_cancellationTokenSource.Token);
            }
            catch
            {
                await DisplayAlert("System Error", "An unexpected error occurred. Please try again.", "OK");
            }
            finally { await SetLoadingState(false); }
        }

        private async Task PerformLogin(CancellationToken cancellationToken)
        {
            string emailVal = Email.Text?.Trim() ?? string.Empty;
            string passwordVal = Password.Text?.Trim() ?? string.Empty;

            string selectedRole = RolePicker.SelectedItem as string ?? "Payobe User";

            var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true };
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                if (selectedRole == "Payobe Officer")
                {
                    string url = "https://payyobe.com/api/v1/login-officer";
                    var payload = new { Email = emailVal, Password = passwordVal };
                    string jsonBody = JsonConvert.SerializeObject(payload);

                    cancellationToken.ThrowIfCancellationRequested();
                    using (var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"))
                    {
                        using (var response = await client.PostAsync(url, content, cancellationToken))
                        {
                            var jsonContent = await response.Content.ReadAsStringAsync();
                            var result = JsonConvert.DeserializeObject<OfficerLoginResponse>(jsonContent);

                            if (result != null && result.statusCode == "00")
                            {
                                await AnimateSuccessState();

                                // Global Storage assignment
                                OfficerId = result.officerId;
                                OfficerName = result.officerName;
                                OfficerCode = result.officerCode;
                                OfficerEmail = result.officerEmail;
                                OfficerPhone = result.officerPhone;
                                OfficerPassword = Password.Text;

                                // Encrypted Hardware persistence
                                await SecureStorage.SetAsync("OfficerId", result.officerId.ToString());
                                await SecureStorage.SetAsync("OfficerName", result.officerName ?? "");
                                await SecureStorage.SetAsync("OfficerCode", result.officerCode ?? "");
                                await SecureStorage.SetAsync("OfficerEmail", result.officerEmail ?? "");
                                await SecureStorage.SetAsync("OfficerPassword", Password.Text);
                                await OfficerSessionManager.SaveSessionAsync(
                                    result.officerId,
                                    result.officerName,
                                    result.officerCode,
                                    result.officerEmail,
                                    result.officerPhone,
                                    result.OfficerPassword
                                );
                               
                                UserDialogs.Instance.Toast($"Welcome Officer, {result.officerName} 👋", TimeSpan.FromSeconds(3));
                                Application.Current.MainPage = new NavigationPage(new Views.Officer.Dashboard());
                            }
                            else
                            {
                                await AnimateErrorState();
                                await DisplayAlert("Access Denied", result?.message ?? "Invalid credentials.", "TRY AGAIN");
                            }
                        }
                    }
                }
                else
                {
                    string url = $"https://payyobe.com/api/v1/login?Email={Uri.EscapeDataString(emailVal)}&Password={Uri.EscapeDataString(passwordVal)}";
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var response = await client.GetAsync(url, cancellationToken))
                    {
                        var jsonContent = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<LoginResponse>(jsonContent);
                        await ProcessLoginResponse(result);
                    }
                }
            }
        }

        private async Task ProcessLoginResponse(LoginResponse result)
        {
            if (result != null && result.status_code == "00")
            {
                await AnimateSuccessState();
                MyfullName = result.name; mymda = result.mda; myemail = result.email; mycourt = result.court; superagents = result.super_agent;
              
                UserDialogs.Instance.Toast($"Welcome, {result.name} 👋", TimeSpan.FromSeconds(3));
                Application.Current.MainPage = new NavigationPage(new Views.Dashboard());
            }
            else
            {
                await AnimateErrorState();
                await DisplayAlert("Access Denied", result?.message ?? "Authentication failed.", "TRY AGAIN");
            }
        }

        private async Task AnimateSuccessState()
        {
            SignInLabel.Text = "✓  ACCESS GRANTED";
            await Task.WhenAll(SignInButton.ScaleTo(1.03, 160, Easing.BounceOut), SignInButton.FadeTo(0.88, 160));
            await Task.Delay(380);
            await SignInButton.ScaleTo(1.0, 160, Easing.CubicOut);
        }

        private async Task AnimateErrorState()
        {
            EmailContainer.BorderColor = ColError; PasswordContainer.BorderColor = ColError;
            await ShakeAsync(FormContainer);
            await Task.Delay(900);
            EmailContainer.BorderColor = ColDefault; PasswordContainer.BorderColor = ColDefault;
        }

        private async void OnForgotPasswordTapped(object sender, EventArgs e)
        {
            try
            {
                await ForgotPasswordLabel.ScaleTo(0.88, 80);
                await ForgotPasswordLabel.ScaleTo(1.00, 80);

                await DisplayAlert("Reset Passcode",
                    "Please contact the Yobe State IRS helpdesk to reset your passcode:\n\n" +
                    "📞 +234-803-052-3208\n" +
                    "📧 support@payyobe.osoftpay.net\n" +
                    "🕐 Mon–Fri  8:00 AM – 5:00 PM",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ForgotPassword error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            try
            {
                await DisplayAlert("Helpdesk",
                    "Yobe State Revenue Service Support:\n\n" +
                    "📞 Phone: +234-803-052-3208\n" +
                    "📧 Email: support@payyobe.osoftpay.net\n" +
                    "🕐 Hours: Monday–Friday  8 AM – 5 PM",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Support tap error: {ex.Message}");
            }
        }
    }

    public class LoginResponse
    {
        public string status_code { get; set; }
        public string message { get; set; }
        public string name { get; set; }
        public string mda { get; set; }
        public string email { get; set; }
        public string court { get; set; }
        public string super_agent { get; set; }
    }

    public class OfficerLoginResponse
    {
        public string statusCode { get; set; }
        public string message { get; set; }
        public int officerId { get; set; }
        public string officerName { get; set; }
        public string officerCode { get; set; }
        public string officerEmail { get; set; }
        public string officerPhone { get; set; }
        public string OfficerPassword { get; set; }
    }
}