using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace PAYYOBE
{
    public partial class MainPage : ContentPage
    {
        // ── Shared state (retained for downstream navigation) ──────────────
        public static string MyfullName { get; set; }
        public static string mymda { get; set; }
        public static string myemail { get; set; }
        public static string mycourt { get; set; }
        public static string superagents { get; set; }

        // ── UI state ───────────────────────────────────────────────────────
        private bool _isLoading = false;
        private bool _passwordVisible = false;
        private CancellationTokenSource _cancellationTokenSource;

        // ── Border colour palette (Midnight Navy / Gold theme) ─────────────
        private static readonly Color ColDefault = Color.FromHex("#162A48");
        private static readonly Color ColFocused = Color.FromHex("#C8941A");   // gold on focus
        private static readonly Color ColValid = Color.FromHex("#1A6B8A");   // steel blue when valid
        private static readonly Color ColError = Color.FromHex("#D95F5F");

        public MainPage()
        {
            InitializeComponent();
        }

        // ─────────────────────────────────────────────────────────────────
        //  PAGE LIFECYCLE
        // ─────────────────────────────────────────────────────────────────

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartEntranceAnimations();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cancellationTokenSource?.Cancel();
        }

        protected override bool OnBackButtonPressed()
        {
            try
            {
                if (_isLoading)
                {
                    _cancellationTokenSource?.Cancel();
                    return true;
                }
                return base.OnBackButtonPressed();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back button error: {ex.Message}");
                return base.OnBackButtonPressed();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  ENTRANCE ANIMATIONS  —  presidential-grade reveal
        // ─────────────────────────────────────────────────────────────────

        private async void StartEntranceAnimations()
        {
            try
            {
                // Reset all sections
                HeaderSection.Opacity = 0;
                HeaderSection.TranslationY = -20;
                FormContainer.Opacity = 0;
                FormContainer.TranslationY = 70;
                FooterSection.Opacity = 0;
                FooterSection.TranslationY = 18;

                // Fire decorative orb rotation
                _ = AnimateOrbs();

                // 1. Header slides down
                await Task.Delay(120);
                await Task.WhenAll(
                    HeaderSection.FadeTo(1, 650, Easing.CubicOut),
                    HeaderSection.TranslateTo(0, 0, 650, Easing.CubicOut));

                // 2. Seal heartbeat
                await SealFrame.ScaleTo(1.07, 220, Easing.SinIn);
                await SealFrame.ScaleTo(1.00, 220, Easing.SinOut);

                // 3. Form card rises
                await Task.Delay(80);
                await Task.WhenAll(
                    FormContainer.FadeTo(1, 580, Easing.CubicOut),
                    FormContainer.TranslateTo(0, 0, 580, Easing.CubicOut));

                // 4. Individual form elements stagger in
                await AnimateFormElements();

                // 5. Footer fades in
                await Task.Delay(80);
                await Task.WhenAll(
                    FooterSection.FadeTo(1, 480, Easing.CubicOut),
                    FooterSection.TranslateTo(0, 0, 480, Easing.CubicOut));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Entrance animation error: {ex.Message}");
            }
        }

        private async Task AnimateOrbs()
        {
            try
            {
                await Task.WhenAll(
                    OrbTopRight.RotateTo(360, 36000, Easing.Linear),
                    OrbBottomLeft.RotateTo(-360, 48000, Easing.Linear));
            }
            catch { /* decorative — swallow */ }
        }

        private async Task AnimateFormElements()
        {
            try
            {
                EmailSection.Opacity = 0;
                EmailSection.TranslationX = -36;
                PasswordSection.Opacity = 0;
                PasswordSection.TranslationX = 36;
                SignInButton.Opacity = 0;
                SignInButton.Scale = 0.90;

                var emailAnim = Task.WhenAll(
                    EmailSection.FadeTo(1, 360, Easing.CubicOut),
                    EmailSection.TranslateTo(0, 0, 360, Easing.CubicOut));

                await Task.Delay(110);

                var passAnim = Task.WhenAll(
                    PasswordSection.FadeTo(1, 360, Easing.CubicOut),
                    PasswordSection.TranslateTo(0, 0, 360, Easing.CubicOut));

                await Task.Delay(110);

                var btnAnim = Task.WhenAll(
                    SignInButton.FadeTo(1, 360, Easing.CubicOut),
                    SignInButton.ScaleTo(1, 360, Easing.BounceOut));

                await Task.WhenAll(emailAnim, passAnim, btnAnim);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Form elements animation: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  PASSWORD TOGGLE
        // ─────────────────────────────────────────────────────────────────

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Password toggle error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  FOCUS EVENTS  — gold border on focus, semantic colour on blur
        // ─────────────────────────────────────────────────────────────────

        private async void OnEmailFocused(object sender, FocusEventArgs e)
        {
            try
            {
                EmailContainer.BorderColor = ColFocused;
                EmailContainer.BorderThickness = 2;
                await EmailContainer.ScaleTo(1.012, 120, Easing.CubicOut);
            }
            catch { }
        }

        private async void OnEmailUnfocused(object sender, FocusEventArgs e)
        {
            try
            {
                await EmailContainer.ScaleTo(1.0, 120, Easing.CubicOut);
                bool valid = IsValidEmail(Email.Text);
                EmailContainer.BorderColor = string.IsNullOrWhiteSpace(Email.Text)
                    ? ColDefault
                    : valid ? ColValid : ColError;
                EmailContainer.BorderThickness = 1;
            }
            catch { }
        }

        private async void OnPasswordFocused(object sender, FocusEventArgs e)
        {
            try
            {
                PasswordContainer.BorderColor = ColFocused;
                PasswordContainer.BorderThickness = 2;
                await PasswordContainer.ScaleTo(1.012, 120, Easing.CubicOut);
            }
            catch { }
        }

        private async void OnPasswordUnfocused(object sender, FocusEventArgs e)
        {
            try
            {
                await PasswordContainer.ScaleTo(1.0, 120, Easing.CubicOut);
                bool valid = IsValidPassword(Password.Text);
                PasswordContainer.BorderColor = string.IsNullOrWhiteSpace(Password.Text)
                    ? ColDefault
                    : valid ? ColValid : ColError;
                PasswordContainer.BorderThickness = 1;
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        //  VALIDATION
        // ─────────────────────────────────────────────────────────────────

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                return new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")
                    .IsMatch(email);
            }
            catch { return false; }
        }

        private static bool IsValidPassword(string password)
            => !string.IsNullOrWhiteSpace(password) && password.Length >= 6;

        private async Task<bool> ValidateInputs()
        {
            bool isValid = true;

            // Email / Staff ID
            if (string.IsNullOrWhiteSpace(Email.Text))
            {
                SetFieldError(EmailContainer, EmailErrorLabel,
                    "Staff ID or email address is required");
                await ShakeAsync(EmailContainer);
                isValid = false;
            }
            else if (!IsValidEmail(Email.Text))
            {
                SetFieldError(EmailContainer, EmailErrorLabel,
                    "Please enter a valid staff email address");
                await ShakeAsync(EmailContainer);
                isValid = false;
            }
            else
            {
                SetFieldValid(EmailContainer, EmailErrorLabel);
            }

            // Passcode
            if (string.IsNullOrWhiteSpace(Password.Text))
            {
                SetFieldError(PasswordContainer, PasswordErrorLabel,
                    "Access passcode is required");
                await ShakeAsync(PasswordContainer);
                isValid = false;
            }
            else if (!IsValidPassword(Password.Text))
            {
                SetFieldError(PasswordContainer, PasswordErrorLabel,
                    "Passcode must be at least 6 characters");
                await ShakeAsync(PasswordContainer);
                isValid = false;
            }
            else
            {
                SetFieldValid(PasswordContainer, PasswordErrorLabel);
            }

            return isValid;
        }

        private void SetFieldError(
            Xamarin.Forms.PancakeView.PancakeView container,
            Label errorLabel,
            string message)
        {
            container.BorderColor = ColError;
            container.BorderThickness = 2;
            errorLabel.Text = message;
            errorLabel.IsVisible = true;
        }

        private void SetFieldValid(
            Xamarin.Forms.PancakeView.PancakeView container,
            Label errorLabel)
        {
            container.BorderColor = ColValid;
            container.BorderThickness = 1;
            errorLabel.IsVisible = false;
        }

        // ─────────────────────────────────────────────────────────────────
        //  ANIMATION HELPERS
        // ─────────────────────────────────────────────────────────────────

        private static async Task ShakeAsync(Xamarin.Forms.View element)
        {
            try
            {
                await element.TranslateTo(-8, 0, 40);
                await element.TranslateTo(8, 0, 40);
                await element.TranslateTo(-5, 0, 36);
                await element.TranslateTo(5, 0, 36);
                await element.TranslateTo(0, 0, 32);
            }
            catch { }
        }

        private async Task AnimateButtonPress()
        {
            try
            {
                await SignInButton.ScaleTo(0.97, 85, Easing.CubicIn);
                await SignInButton.ScaleTo(1.00, 85, Easing.CubicOut);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        //  LOADING STATE
        // ─────────────────────────────────────────────────────────────────

        private async Task SetLoadingState(bool isLoading)
        {
            try
            {
                _isLoading = isLoading;

                LoadingIndicator.IsVisible = isLoading;
                LoadingIndicator.IsRunning = isLoading;

                SignInLabel.Text = isLoading ? "AUTHENTICATING..." : "ACCESS TAX PORTAL";

                SignInButton.IsEnabled = !isLoading;
                Email.IsEnabled = !isLoading;
                Password.IsEnabled = !isLoading;
                PasswordToggleBtn.IsEnabled = !isLoading;

                await SignInButton.FadeTo(isLoading ? 0.65 : 1.00, 140);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetLoadingState error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  SIGN-IN BUTTON HANDLER
        // ─────────────────────────────────────────────────────────────────

        private async void Button_Clicked(object sender, EventArgs e)
        {
            if (_isLoading) return;

            try
            {
                await AnimateButtonPress();

                if (!await ValidateInputs()) return;

                await SetLoadingState(true);

                _cancellationTokenSource = new CancellationTokenSource();
                await PerformLogin(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // User cancelled — silent
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Button_Clicked error: {ex.Message}");
                await DisplayAlert("System Error",
                    "An unexpected error occurred. Please try again.", "OK");
            }
            finally
            {
                await SetLoadingState(false);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  LOGIN LOGIC
        // ─────────────────────────────────────────────────────────────────

        private async Task PerformLogin(CancellationToken cancellationToken)
        {
            try
            {
                string emailVal = Email.Text?.Trim() ?? string.Empty;
                string passwordVal = Password.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(emailVal) || string.IsNullOrEmpty(passwordVal))
                {
                    await DisplayAlert("Incomplete", "Please fill in all required fields.", "OK");
                    return;
                }

                string url = "https://payyobe.com/api/v1/login"
                    + $"?Email={Uri.EscapeDataString(emailVal)}"
                    + $"&Password={Uri.EscapeDataString(passwordVal)}";

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        (message, cert, chain, errors) => true
                };

                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12 |
                        SecurityProtocolType.Tls11 |
                        SecurityProtocolType.Tls;

                    cancellationToken.ThrowIfCancellationRequested();

                    using (var response = await client.GetAsync(url, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var jsonContent = await response.Content.ReadAsStringAsync();

                        if (string.IsNullOrWhiteSpace(jsonContent))
                        {
                            await DisplayAlert("Server Error",
                                "Empty response from the server. Please try again.", "OK");
                            return;
                        }

                        var result = JsonConvert.DeserializeObject<LoginResponse>(jsonContent);

                        if (result == null)
                        {
                            await DisplayAlert("Server Error",
                                "Invalid server response. Please try again.", "OK");
                            return;
                        }

                        await ProcessLoginResponse(result);
                    }
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                await DisplayAlert("Connection Timeout",
                    "The server did not respond in time. Check your connection and retry.", "OK");
            }
            catch (TaskCanceledException)
            {
                // Cancelled by user — silent
            }
            catch (HttpRequestException ex)
            {
                await DisplayAlert("Network Error",
                    "Cannot reach the server. Please check your internet connection.", "OK");
                System.Diagnostics.Debug.WriteLine($"Network error: {ex.Message}");
            }
            catch (JsonException ex)
            {
                await DisplayAlert("Data Error",
                    "Invalid response format received. Please try again.", "OK");
                System.Diagnostics.Debug.WriteLine($"JSON error: {ex.Message}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("System Error",
                    "An unexpected error occurred. Please try again.", "OK");
                System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
            }
        }

        private async Task ProcessLoginResponse(LoginResponse result)
        {
            try
            {
                if (result.status_code == "00")
                {
                    // ── Authenticated ────────────────────────────────────────
                    await AnimateSuccessState();

                    MyfullName = result.name;
                    mymda = result.mda;
                    myemail = result.email;
                    mycourt = result.court;
                    superagents = result.super_agent;
                    UserDialogs.Instance.Toast(
                        $"Welcome, {result.name} 👋",
                        TimeSpan.FromSeconds(3));

                    Application.Current.MainPage =
                        new NavigationPage(new Views.Dashboard());
                }
                else
                {
                    // ── Rejected ─────────────────────────────────────────────
                    await AnimateErrorState();

                    string msg = !string.IsNullOrWhiteSpace(result.message)
                        ? result.message
                        : "Authentication failed. Verify your credentials and retry.";

                    await DisplayAlert("Access Denied", msg, "TRY AGAIN");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessLoginResponse error: {ex.Message}");
                await DisplayAlert("Error",
                    "An error occurred while processing the server response.", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  SUCCESS / ERROR ANIMATIONS
        // ─────────────────────────────────────────────────────────────────

        private async Task AnimateSuccessState()
        {
            try
            {
                SignInLabel.Text = "✓  ACCESS GRANTED";

                await Task.WhenAll(
                    SignInButton.ScaleTo(1.03, 160, Easing.BounceOut),
                    SignInButton.FadeTo(0.88, 160));

                await Task.Delay(380);
                await SignInButton.ScaleTo(1.0, 160, Easing.CubicOut);
            }
            catch { }
        }

        private async Task AnimateErrorState()
        {
            try
            {
                EmailContainer.BorderColor = ColError;
                PasswordContainer.BorderColor = ColError;

                await ShakeAsync(FormContainer);

                await Task.Delay(900);
                ResetBorderColours();
            }
            catch { }
        }

        private void ResetBorderColours()
        {
            EmailContainer.BorderColor = ColDefault;
            PasswordContainer.BorderColor = ColDefault;
            EmailContainer.BorderThickness = 1;
            PasswordContainer.BorderThickness = 1;
        }

        // ─────────────────────────────────────────────────────────────────
        //  OTHER TAP HANDLERS
        // ─────────────────────────────────────────────────────────────────

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

    // ═══════════════════════════════════════════════════════════════════
    //  RESPONSE MODEL
    // ═══════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════
    //  LEGACY TRIGGER (backward-compat)
    // ═══════════════════════════════════════════════════════════════════

    public class ShowPasswordTriggerAction : TriggerAction<ImageButton>
    {
        public string ShowIcon { get; set; }
        public string HideIcon { get; set; }
        public bool HidePassword { get; set; } = true;

        protected override async void Invoke(ImageButton sender)
        {
            try
            {
                HidePassword = !HidePassword;
                sender.Source = HidePassword ? HideIcon : ShowIcon;
                await sender.ScaleTo(0.8, 80);
                await sender.ScaleTo(1.0, 80);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowPasswordTriggerAction error: {ex.Message}");
            }
        }
    }
}