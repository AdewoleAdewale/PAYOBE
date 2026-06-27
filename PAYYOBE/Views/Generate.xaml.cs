using Acr.UserDialogs;
using Newtonsoft.Json;
using PAYYOBE.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PancakeView;
using Xamarin.Forms.Xaml;

namespace PAYYOBE.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Generate : ContentPage
    {
        private List<PaymentData> _paymentData;

        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private readonly Dictionary<string, bool> _fieldValidationStatus;
        private readonly Dictionary<string, string> _validationErrors;

        private bool _isProcessing;
        private int _completedFields;

        // Fields: fullName, email, phone, category, amount = 5
        private const int TotalFields = 5;

        private const uint SheetAnimationDuration = 300;
        private const uint FieldAnimationDuration = 200;

        private bool _isPrinting = false;
        private ReceiptData _lastReceiptData = null;
        private string _lastInvoiceNumber = null;
        private InvoiceResult _lastInvoiceResult = null;
        private bool _isNavigatingAway = false;

        // ─────────────────────────────────────────────────────────────────────
        //  SINGLE SHARED HTTP CLIENT
        //  ✅ FIX: Added Accept: application/json default header here so every
        //          request carries it — without it IIS/ASP.NET returns HTML error
        //          pages instead of JSON, causing the "DOCTYPE html" response.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly HttpClient _client = new HttpClient(
      new HttpClientHandler
      {
          ServerCertificateCustomValidationCallback = (m, c, ch, e) => true,
          AllowAutoRedirect = true,          // ✅ Follow auth redirects
          MaxAutomaticRedirections = 3       // ✅ But cap at 3
      })
        {
            Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
        };

        static Generate()
        {
            // ✅ FIX: Tell the server we only accept JSON — prevents HTML error responses
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BINDABLE PROPERTIES
        // ─────────────────────────────────────────────────────────────────────

        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); UpdateButtonState(); }
        }

        public int CompletedFields
        {
            get => _completedFields;
            set { _completedFields = value; OnPropertyChanged(); UpdateProgressUI(); UpdateButtonState(); }
        }

        public Generate()
        {
            InitializeComponent();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            BindingContext = this;

            _fieldValidationStatus = new Dictionary<string, bool>
            {
                ["fullName"] = false,
                ["email"] = false,
                ["phone"] = false,
                ["category"] = false,
                ["amount"] = false
            };
            _validationErrors = new Dictionary<string, string>();

            StartEntranceAnimations();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Populate header agent info
            if (headerAgentName != null)
                headerAgentName.Text = MainPage.MyfullName ?? "Agent";
            if (headerMdaName != null)
            {
                var mda = MainPage.mymda ?? "—";
                headerMdaName.Text = mda.Length > 35 ? mda.Substring(0, 35) + "…" : mda;
            }

            if (_paymentData == null)
                await InitializePaymentCategoriesAsync();
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); }
            catch { }
        }


        protected override bool OnBackButtonPressed()
        {
            if (successSheet != null && successSheet.IsVisible)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await HideSheet(successSheet, sheetOverlay));
                return true;
            }
            if (errorSheet != null && errorSheet.IsVisible)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await HideSheet(errorSheet, sheetOverlay));
                return true;
            }
            if (_isNavigatingAway) return true;
            _isNavigatingAway = true;
            if (IsProcessing) IsProcessing = false;
            return base.OnBackButtonPressed();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ENTRANCE ANIMATION
        // ─────────────────────────────────────────────────────────────────────

        private async void StartEntranceAnimations()
        {
            try
            {
                headerSection.Opacity = 0;
                headerSection.TranslationY = -40;
                mainFormContainer.Opacity = 0;
                mainFormContainer.TranslationY = 80;

                await Task.Delay(80);

                _ = headerSection.FadeTo(1, 700, Easing.CubicOut);
                _ = headerSection.TranslateTo(0, 0, 700, Easing.CubicOut);

                await Task.Delay(180);

                _ = mainFormContainer.FadeTo(1, 700, Easing.CubicOut);
                _ = mainFormContainer.TranslateTo(0, 0, 700, Easing.CubicOut);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Generate] Animation: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAYMENT CATEGORIES
        // ─────────────────────────────────────────────────────────────────────

        private async Task InitializePaymentCategoriesAsync()
        {
            try
            {
                PIC.Title = "⏳  Loading categories…";
                PIC.IsEnabled = false;

                var items = await FetchPaymentItemsWithRetryAsync();
                _paymentData = items;

                PIC.ItemsSource = items;
                PIC.ItemDisplayBinding = new Binding(nameof(PaymentData.ServiceName));
                PIC.Title = "Select Payment Category";
                PIC.IsEnabled = true;

                Debug.WriteLine($"[Generate] Loaded {items.Count} categories.");
            }
            catch (Exception ex)
            {
                PIC.Title = "⚠  Failed to load – tap to retry";
                PIC.IsEnabled = false;
                await DisplayAlert("Error",
                    "Failed to load payment categories. Please check your internet connection.",
                    "OK");
                Debug.WriteLine($"[Generate] Categories: {ex}");
            }
        }

        private async Task<List<PaymentData>> FetchPaymentItemsWithRetryAsync()
        {
            string url = $"https://payyobe.com/api/v1/PaymentItems?Email={Uri.EscapeDataString(MainPage.myemail ?? "")}";

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    using (var response = await _client.GetAsync(url))
                    {
                        var raw = await response.Content.ReadAsStringAsync();

                        // ✅ Guard: if server returns HTML, it's a redirect or auth wall
                        if (IsHtmlResponse(raw))
                            throw new InvalidDataException("Server returned an HTML page instead of JSON. Check authentication.");

                        response.EnsureSuccessStatusCode();

                        if (string.IsNullOrWhiteSpace(raw))
                            throw new InvalidDataException("Empty response");

                        var items = JsonConvert.DeserializeObject<List<PaymentData>>(raw);
                        if (items == null || !items.Any())
                            throw new InvalidDataException("No payment items found");

                        return items;
                    }
                }
                catch (Exception ex) when (attempt < MAX_RETRY_ATTEMPTS)
                {
                    Debug.WriteLine($"[Generate] Attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
            throw new InvalidOperationException(
                $"Failed after {MAX_RETRY_ATTEMPTS} attempts");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FIELD EVENTS
        // ─────────────────────────────────────────────────────────────────────

        private void OnFullNameTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateField("fullName", e.NewTextValue, ValidateFullName);
            UpdateLivePreview();
        }

        private void OnEmailTextChanged(object sender, TextChangedEventArgs e)
            => ValidateField("email", e.NewTextValue, ValidateEmail);

        private void OnPhoneTextChanged(object sender, TextChangedEventArgs e)
            => ValidateField("phone", e.NewTextValue, ValidatePhone);

        private void OnAmountTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateField("amount", e.NewTextValue, ValidateAmount);
            UpdateAmountPreview(e.NewTextValue);
            UpdateLivePreview();
        }

        private void OnFullNameUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("fullName", fullname.Text, ValidateFullName,
               fullNameFrame, fullNameError, fullNameValidIcon);

        private void OnEmailUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("email", email.Text, ValidateEmail,
               EmailFrame, EmailError, EmailValidIcon);

        private void OnPhoneUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("phone", userphonenumber.Text, ValidatePhone,
               phoneFrame, phoneError, phoneValidIcon);

        private void OnAmountUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("amount", useramount.Text, ValidateAmount,
               amountFrame, amountError, amountValidIcon);

        // ─────────────────────────────────────────────────────────────────────
        //  CATEGORY SELECTION
        //  ✅ NEW: Auto-fill amount from the selected PaymentData.Amount
        // ─────────────────────────────────────────────────────────────────────

        private void OnCategorySelectionChanged(object sender, EventArgs e)
        {
            var picker = sender as Picker;
            bool isValid = picker?.SelectedIndex >= 0;

            UpdateFieldValidation("category", isValid,
                isValid ? string.Empty : "Please select a payment category");

            if (isValid)
            {
                AnimateSuccessValidation(categoryFrame, categoryValidIcon);

                var selected = _paymentData?[picker.SelectedIndex];
                if (selected != null)
                {
                    // Auto-fill amount only if the service has a fixed amount > 0
                    if (selected.Amount > 0)
                    {
                        useramount.Text = selected.Amount.ToString("F0");
                        ValidateField("amount", useramount.Text, ValidateAmount);
                        UpdateAmountPreview(useramount.Text);
                    }

                    // Update live preview category label
                    if (previewCategory != null)
                        previewCategory.Text = selected.ServiceName;
                }

                UpdateLivePreview();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  VALIDATORS
        // ─────────────────────────────────────────────────────────────────────

        private (bool, string) ValidateFullName(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Full name is required");
            if (v.Trim().Length < 2) return (false, "Name must be at least 2 characters");
            if (!Regex.IsMatch(v.Trim(), @"^[a-zA-Z\s'\-\.]+$"))
                return (false, "Name contains invalid characters");
            if (v.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length < 2)
                return (false, "Please enter first and last name");
            return (true, string.Empty);
        }

        private (bool, string) ValidateEmail(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Email address is required");
            v = v.Trim();
            if (!Regex.IsMatch(v, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                return (false, "Please enter a valid email address");
            return (true, string.Empty);
        }

        private (bool, string) ValidatePhone(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Phone number is required");
            var digits = Regex.Replace(v, @"[^\d]", "");
            if (digits.Length < 10) return (false, "Phone must be at least 10 digits");
            if (digits.Length > 13) return (false, "Phone cannot exceed 13 digits");
            return (true, string.Empty);
        }

        private (bool, string) ValidateAmount(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Amount is required");
            var clean = Regex.Replace(v, @"[₦,\s]", "");
            if (!decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount))
                return (false, "Invalid amount format");
            if (amount <= 0) return (false, "Amount must be greater than ₦0");
            if (amount > 10_000_000m) return (false, "Amount cannot exceed ₦10,000,000");
            return (true, string.Empty);
        }

        private void ValidateField(string fieldName, string value,
            Func<string, (bool, string)> validator)
        {
            var (isValid, error) = validator(value);
            UpdateFieldValidation(fieldName, isValid, error);
        }

        private void UpdateFieldValidation(string fieldName, bool isValid, string error)
        {
            bool wasValid = _fieldValidationStatus.ContainsKey(fieldName) &&
                            _fieldValidationStatus[fieldName];
            _fieldValidationStatus[fieldName] = isValid;

            if (isValid) _validationErrors.Remove(fieldName);
            else _validationErrors[fieldName] = error;

            if (!wasValid && isValid) CompletedFields++;
            else if (wasValid && !isValid) CompletedFields--;
        }

        private async void AnimateFieldValidation(string fieldName, string value,
            Func<string, (bool, string)> validator,
            PancakeView frame, Label errorLabel, Label validIcon)
        {
            try
            {
                var (isValid, error) = validator(value);
                UpdateFieldValidation(fieldName, isValid, error);
                if (isValid)
                {
                    await AnimateSuccessValidation(frame, validIcon);
                    errorLabel.IsVisible = false;
                }
                else
                {
                    await AnimateErrorValidation(frame, errorLabel, error);
                    validIcon.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Generate] AnimateField {fieldName}: {ex.Message}");
            }
        }

        private async Task AnimateSuccessValidation(PancakeView frame, Label validIcon)
        {
            frame.Style = (Style)Resources["EntryContainerSuccessStyle"];
            await frame.ScaleTo(1.03, FieldAnimationDuration / 2, Easing.CubicOut);
            await frame.ScaleTo(1.0, FieldAnimationDuration / 2, Easing.CubicIn);
            validIcon.IsVisible = true;
            validIcon.Scale = 0;
            await validIcon.ScaleTo(1.2, FieldAnimationDuration, Easing.BounceOut);
            await validIcon.ScaleTo(1.0, FieldAnimationDuration / 2, Easing.CubicIn);
        }

        private async Task AnimateErrorValidation(PancakeView frame, Label errorLabel, string error)
        {
            frame.Style = (Style)Resources["EntryContainerErrorStyle"];
            await frame.TranslateTo(-8, 0, 40);
            await frame.TranslateTo(8, 0, 40);
            await frame.TranslateTo(-4, 0, 40);
            await frame.TranslateTo(0, 0, 40);
            errorLabel.Text = error;
            errorLabel.IsVisible = true;
            errorLabel.Opacity = 0;
            await errorLabel.FadeTo(1, FieldAnimationDuration);
        }

        private void UpdateAmountPreview(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) { amountFormatted.IsVisible = false; return; }
                var clean = Regex.Replace(value, @"[₦,\s]", "");
                if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) && amount > 0)
                {
                    amountFormatted.Text = $"₦{amount:N2}";
                    amountFormatted.IsVisible = true;
                }
                else amountFormatted.IsVisible = false;
            }
            catch { amountFormatted.IsVisible = false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LIVE PREVIEW CARD
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateLivePreview()
        {
            try
            {
                if (previewCard == null) return;

                string name = fullname?.Text?.Trim() ?? "";
                string amtRaw = useramount?.Text ?? "";
                string cat = PIC?.SelectedIndex >= 0 && _paymentData != null
                                ? _paymentData[PIC.SelectedIndex].ServiceName
                                : "";

                bool hasAnyData = !string.IsNullOrEmpty(name)
                               || !string.IsNullOrEmpty(amtRaw)
                               || !string.IsNullOrEmpty(cat);

                previewCard.IsVisible = hasAnyData;

                if (previewName != null)
                    previewName.Text = string.IsNullOrEmpty(name) ? "—" : name;

                if (previewCategory != null)
                    previewCategory.Text = string.IsNullOrEmpty(cat) ? "—" : cat;

                if (previewAmount != null)
                {
                    var clean = Regex.Replace(amtRaw, @"[₦,\s]", "");
                    if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal a) && a > 0)
                        previewAmount.Text = $"₦{a:N0}";
                    else
                        previewAmount.Text = "—";
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[Generate] LivePreview: {ex.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PROGRESS / BUTTON
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateProgressUI()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                int done = CompletedFields;
                progressText.Text = $"{done}/{TotalFields} fields completed";
                progressText.TextColor = done == TotalFields
                    ? Color.FromHex("#27AE60")
                    : done >= 3 ? Color.FromHex("#F39C12")
                                : Color.FromHex("#E74C3C");

                // Step dot visual feedback
                UpdateStepDot(step1Dot, done >= 1);
                UpdateStepDot(step2Dot, done >= 2);
                UpdateStepDot(step3Dot, done >= 3);
                UpdateStepDot(step4Dot, done >= 4);
                UpdateStepDot(step5Dot, done >= 5);
            });
        }

        private void UpdateStepDot(BoxView dot, bool active)
        {
            if (dot == null) return;
            dot.Color = active ? Color.FromHex("#27AE60") : Color.FromHex("#D0E4D0");
        }

        private void UpdateButtonState()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                bool canGenerate = CompletedFields == TotalFields && !IsProcessing;
                generateInvoiceBtn.IsEnabled = canGenerate;
                generateInvoiceBtn.Opacity = canGenerate ? 1.0 : 0.45;

                if (IsProcessing)
                {
                    buttonLoadingIndicator.IsRunning = true;
                    buttonLoadingIndicator.IsVisible = true;
                    buttonLabel.Text = "GENERATING…";
                    buttonIcon.IsVisible = false;
                }
                else
                {
                    buttonLoadingIndicator.IsRunning = false;
                    buttonLoadingIndicator.IsVisible = false;
                    buttonLabel.Text = "GENERATE INVOICE";
                    buttonIcon.IsVisible = true;
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GENERATE INVOICE — tap handler
        // ─────────────────────────────────────────────────────────────────────

        private async void OnGenerateInvoiceTapped(object sender, EventArgs e)
        {
            if (IsProcessing || CompletedFields != TotalFields) return;

            try
            {
                IsProcessing = true;
                HideReprintButton();
                _lastReceiptData = null;
                _lastInvoiceResult = null;
                _lastInvoiceNumber = null;

                if (!await PerformFinalValidation()) { IsProcessing = false; return; }

                var invoiceData = CreateInvoiceData();
                var result = await GenerateInvoiceAsync(invoiceData);

                if (result.Success) await ShowSuccessSheet(result.Data);
                else await ShowErrorSheet(result.ErrorMessage, result.ErrorDetails);
            }
            catch (Exception ex)
            {
                await ShowErrorSheet("Unexpected Error", ex.Message);
                Debug.WriteLine($"[Generate] OnGenerateInvoiceTapped: {ex}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task<bool> PerformFinalValidation()
        {
            var errors = new List<string>();

            var checks = new[]
            {
                (fullname.Text,         (Func<string,(bool,string)>)ValidateFullName),
                (email.Text,            ValidateEmail),
                (userphonenumber.Text,  ValidatePhone),
                (useramount.Text,       ValidateAmount),
            };

            foreach (var (val, fn) in checks)
            {
                var (ok, err) = fn(val);
                if (!ok) errors.Add(err);
            }

            if (PIC.SelectedIndex < 0)
                errors.Add("Please select a payment category");

            if (errors.Any())
            {
                await ShowErrorSheet("Validation Failed",
                    "• " + string.Join("\n• ", errors));
                return false;
            }
            return true;
        }

        private InvoiceRequest CreateInvoiceData()
        {
            var clean = Regex.Replace(useramount.Text ?? "0", @"[₦,\s]", "");
            decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount);

            return new InvoiceRequest
            {
                FullName = fullname.Text?.Trim(),
                Email = email.Text?.Trim(),
                PhoneNumber = userphonenumber.Text?.Trim(),
                PaymentItem = _paymentData[PIC.SelectedIndex].ServiceName,
                Amount = amount
            };
        }


        private async Task<ApiResponse<InvoiceResult>> GenerateInvoiceAsync(InvoiceRequest request)
        {
            try
            {
                var payload = new
                {
                    payer_email = request.Email ?? "",
                    Payer_Name = request.FullName ?? "",
                    payer_phone = request.PhoneNumber ?? "",
                    Service_Name = request.PaymentItem ?? "",
                    Amount = (int)Math.Round(request.Amount)
                };

                var jsonBody = JsonConvert.SerializeObject(payload);
                Debug.WriteLine($"[Generate] POST payload: {jsonBody}");

                string agentEmail = Uri.EscapeDataString(MainPage.myemail ?? "");
                using (var reqMsg = new HttpRequestMessage(HttpMethod.Post,
                    $"https://payyobe.com/api/v1/GenerateInvoice?Email={agentEmail}"))
                {
                    reqMsg.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    // ✅ Explicit Accept header on the message (belt-and-suspenders)
                    reqMsg.Headers.Accept.Clear();
                    reqMsg.Headers.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json"));

                    using (var response = await _client.SendAsync(reqMsg))
                    {
                        var raw = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"[Generate] HTTP {(int)response.StatusCode}: {raw?.Substring(0, Math.Min(raw?.Length ?? 0, 400))}");

                        // ✅ FIX 3: Catch 3xx redirects (AllowAutoRedirect = false)
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            var location = response.Headers.Location?.ToString() ?? "unknown";
                            return Fail($"Server redirected the request (HTTP {(int)response.StatusCode}). " +
                                        $"Check API URL. Redirect → {location}");
                        }

                        if (string.IsNullOrWhiteSpace(raw))
                            return Fail("Empty response from server. Please try again.");

                        // ✅ FIX 2: Detect HTML response before trying to parse JSON
                        if (IsHtmlResponse(raw))
                        {
                            Debug.WriteLine("[Generate] ❌ Server returned HTML — likely auth wall or wrong endpoint.");
                            return Fail(
                                "Server returned an HTML page instead of a JSON response. " +
                                "This usually means the request was blocked or redirected. " +
                                "Please check your network connection and try again.",
                                $"HTTP {(int)response.StatusCode} — HTML body received (first 200 chars):\n{raw.Substring(0, Math.Min(raw.Length, 200))}");
                        }

                        InvoiceResult result;
                        try
                        {
                            result = JsonConvert.DeserializeObject<InvoiceResult>(raw);
                        }
                        catch (JsonException jex)
                        {
                            Debug.WriteLine($"[Generate] JSON parse error: {jex.Message}");
                            return Fail("Could not parse server response. Please try again.", raw);
                        }

                        if (result == null)
                            return Fail("Null response from server.", raw);

                        // status_code "00" = success per PAYYOBE API contract
                        bool isSuccess = string.Equals(result.status_code, "00", StringComparison.Ordinal);

                        if (isSuccess && !string.IsNullOrWhiteSpace(result.message))
                        {
                            result.ReferenceNumber = ExtractReferenceNumber(result.message);
                            Debug.WriteLine($"[Generate] ✅ Invoice OK. Ref: {result.ReferenceNumber}");
                            return new ApiResponse<InvoiceResult> { Success = true, Data = result };
                        }

                        // Non-00 status: surface the server's message
                        string errMsg = result.message;
                        if (string.IsNullOrWhiteSpace(errMsg))
                        {
                            try
                            {
                                var errObj = JsonConvert.DeserializeObject<ErrorResponse>(raw);
                                errMsg = errObj?.message ?? errObj?.error;
                            }
                            catch { /* ignore */ }
                        }
                        if (string.IsNullOrWhiteSpace(errMsg))
                            errMsg = $"Server error (HTTP {(int)response.StatusCode})";

                        return Fail(errMsg, raw);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return Fail("Request timed out. Please check your connection and try again.");
            }
            catch (HttpRequestException ex)
            {
                return Fail("Network error. Please check your internet connection.", ex.Message);
            }
            catch (Exception ex)
            {
                return Fail("Unexpected error occurred.", ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HTML RESPONSE GUARD
        // ─────────────────────────────────────────────────────────────────────

        private static bool IsHtmlResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;
            var trimmed = body.TrimStart();
            return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<!-", StringComparison.OrdinalIgnoreCase);
        }

        private static ApiResponse<InvoiceResult> Fail(string msg, string details = null)
            => new ApiResponse<InvoiceResult>
            {
                Success = false,
                ErrorMessage = msg,
                ErrorDetails = details
            };

        // ─────────────────────────────────────────────────────────────────────
        //  SUCCESS SHEET
        // ─────────────────────────────────────────────────────────────────────

        private async Task ShowSuccessSheet(InvoiceResult result)
        {
            try
            {
                _lastInvoiceNumber = result.ReferenceNumber;
                _lastInvoiceResult = result;

                summaryName.Text = fullname.Text;
                rrr.Text = result.ReferenceNumber;
                summaryCategory.Text = PIC.Items[PIC.SelectedIndex];

                var clean = Regex.Replace(useramount.Text ?? "0", @"[₦,\s]", "");
                decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amt);
                summaryAmount.Text = $"₦{amt:N0}";

                var receipt = BuildReceiptData(result, isReprint: false);
                _lastReceiptData = receipt;

                await AttemptPrintAsync(receipt, isReprint: false);
                await ShowSheet(successSheet, sheetOverlay);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Generate] ShowSuccessSheet: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TAP INVOICE NUMBER — COPY
        // ─────────────────────────────────────────────────────────────────────

        private async void OnInvoiceNumberTapped(object sender, EventArgs e)
        {
            try
            {
                var invoiceNo = _lastInvoiceNumber ?? rrr?.Text;
                if (string.IsNullOrWhiteSpace(invoiceNo)) return;

                await Clipboard.SetTextAsync(invoiceNo);

                var origColor = rrr.TextColor;
                var origText = rrr.Text;
                rrr.TextColor = Color.FromHex("#27AE60");
                rrr.Text = "✓  Copied!";
                await Task.Delay(1400);
                rrr.Text = origText;
                rrr.TextColor = origColor;

                UserDialogs.Instance.Toast($"Copied: {invoiceNo}", TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Generate] CopyTap: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RECEIPT BUILDER
        // ─────────────────────────────────────────────────────────────────────

        private ReceiptData BuildReceiptData(InvoiceResult result, bool isReprint)
        {
            decimal amt = 0m;
            var clean = Regex.Replace(useramount.Text ?? "0", @"[₦,\s]", "");
            decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out amt);

            var items = new List<ReceiptItem>
            {
                new ReceiptItem { Description = "INVOICE NUMBER", Amount = 0m,
                    SubText = result.ReferenceNumber ?? result.InvoiceId ?? "N/A" }
            };

            if (isReprint)
                items.Add(new ReceiptItem
                {
                    Description = "** REPRINTED COPY **",
                    Amount = 0m,
                    SubText = $"Reprinted: {DateTime.Now:dd MMM yyyy HH:mm}"
                });

            items.Add(new ReceiptItem { Description = "Payment Item", Amount = 0m, SubText = summaryCategory.Text ?? "N/A" });
            items.Add(new ReceiptItem { Description = "Payer Name", Amount = 0m, SubText = fullname.Text?.Trim() ?? "N/A" });
            items.Add(new ReceiptItem { Description = "Amount Due", Amount = amt });

            return new ReceiptData
            {
                StoreName = "YOBE STATE REVENUE SERVICES [YIRS]",
                StorePhone = "Contact: +234 803 052 3208",
                ReceiptNumber = result.ReferenceNumber ?? "N/A",
                AgentName = MainPage.MyfullName ?? "N/A",
                CollectionPoint = MainPage.mymda ?? "N/A",
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = 0m,
                FooterLine1 = "Present this invoice at the payment counter",
                FooterLine2 = "POWERED BY OSOFTPAY",
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PRINT
        // ─────────────────────────────────────────────────────────────────────

        private async Task AttemptPrintAsync(ReceiptData receipt, bool isReprint)
        {
            if (_isPrinting) return;
            _isPrinting = true;

            try
            {
                bool granted = await BluetoothPermissionHelper.RequestAsync();
                if (!granted)
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast("Bluetooth permission denied.", TimeSpan.FromSeconds(6));
                        ShowReprintButton();
                    });
                    return;
                }

                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.ChunkStarted:
                                UserDialogs.Instance.Toast($"Printing {p.ChunkName}…", TimeSpan.FromSeconds(2));
                                break;
                            case PrintProgressStatus.SessionCompleted:
                                HideReprintButton();
                                UserDialogs.Instance.Toast(
                                    isReprint ? "Reprinted successfully!" : "Receipt printed.",
                                    TimeSpan.FromSeconds(4));
                                break;
                            case PrintProgressStatus.ChunkFailed:
                                ShowReprintButton();
                                UserDialogs.Instance.Toast($"Could not print {p.ChunkName}.", TimeSpan.FromSeconds(5));
                                break;
                        }
                    }));

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                    await App.PrintJobManager.DeleteJobAsync(job.JobId);
                    Device.BeginInvokeOnMainThread(HideReprintButton);
                }
                catch (PrinterException pex)
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast($"Print failed: {pex.Message}. Tap Reprint to retry.", TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                catch (OperationCanceledException)
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast("Print timed out. Tap Reprint when ready.", TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Generate] Print: {ex}");
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast("Printer not connected. Tap Reprint to retry.", TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                finally { cts.Dispose(); }
            }
            finally { _isPrinting = false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  REPRINT
        // ─────────────────────────────────────────────────────────────────────

        private async void OnReprintInvoiceClicked(object sender, EventArgs e)
        {
            if (_lastInvoiceResult == null)
            {
                UserDialogs.Instance.Toast("No invoice data available.", TimeSpan.FromSeconds(4));
                return;
            }
            ReprintInvoiceButton.IsEnabled = false;
            try
            {
                var receipt = BuildReceiptData(_lastInvoiceResult, isReprint: true);
                await AttemptPrintAsync(receipt, isReprint: true);
            }
            finally { ReprintInvoiceButton.IsEnabled = true; }
        }

        private void ShowReprintButton()
        {
            try { ReprintInvoiceButton.IsVisible = true; ReprintInvoiceButton.FadeTo(1, 300, Easing.CubicOut); }
            catch { }
        }

        private void HideReprintButton()
        {
            try { ReprintInvoiceButton.IsVisible = false; } catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHEET HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private async Task ShowErrorSheet(string errorMessage, string errorDetails = null)
        {
            try
            {
                errorDescription.Text = errorMessage;
                bool hasDetails = !string.IsNullOrWhiteSpace(errorDetails);
                errorDetailsContainer.IsVisible = hasDetails;

                if (hasDetails)
                {
                    errorDetails_Label.Text = errorDetails.Length > 300
                        ? errorDetails.Substring(0, 300) + "…"
                        : errorDetails;
                }

                await ShowSheet(errorSheet, sheetOverlay);
            }
            catch (Exception ex) { Debug.WriteLine($"[Generate] ShowErrorSheet: {ex}"); }
        }

        private async Task ShowSheet(PancakeView sheet, Grid overlay)
        {
            try
            {
                overlay.IsVisible = true;
                await overlay.FadeTo(1, SheetAnimationDuration);
                sheet.IsVisible = true;
                await sheet.TranslateTo(0, 0, SheetAnimationDuration, Easing.CubicOut);
            }
            catch (Exception ex) { Debug.WriteLine($"[Generate] ShowSheet: {ex.Message}"); }
        }

        private async Task HideSheet(PancakeView sheet, Grid overlay)
        {
            try
            {
                await sheet.TranslateTo(0, 1000, SheetAnimationDuration, Easing.CubicIn);
                sheet.IsVisible = false;
                await overlay.FadeTo(0, SheetAnimationDuration);
                overlay.IsVisible = false;
            }
            catch (Exception ex) { Debug.WriteLine($"[Generate] HideSheet: {ex.Message}"); }
        }

        private async void OnOverlayTapped(object sender, EventArgs e)
        {
            if (successSheet.IsVisible) await HideSheet(successSheet, sheetOverlay);
            else if (errorSheet.IsVisible) await HideSheet(errorSheet, sheetOverlay);
        }

        private async void OnCloseSheetTapped(object sender, EventArgs e)
            => await HideSheet(successSheet, sheetOverlay);

        private async void OnCloseErrorSheetTapped(object sender, EventArgs e)
            => await HideSheet(errorSheet, sheetOverlay);

        private async void OnGenerateAnotherTapped(object sender, EventArgs e)
        {
            await HideSheet(successSheet, sheetOverlay);
            ClearForm();
        }

        private async void OnContinueTapped(object sender, EventArgs e)
            => await HideSheet(successSheet, sheetOverlay);

        private async void OnTryAgainTapped(object sender, EventArgs e)
            => await HideSheet(errorSheet, sheetOverlay);

        private void ClearForm()
        {
            try
            {
                fullname.Text = string.Empty;
                email.Text = string.Empty;
                userphonenumber.Text = string.Empty;
                useramount.Text = string.Empty;
                PIC.SelectedIndex = -1;

                foreach (var k in _fieldValidationStatus.Keys.ToList())
                    _fieldValidationStatus[k] = false;

                _validationErrors.Clear();
                CompletedFields = 0;
                _lastReceiptData = null;
                _lastInvoiceResult = null;
                _lastInvoiceNumber = null;

                ResetFieldStyles();
                HideAllValidationMessages();
                HideReprintButton();

                if (previewCard != null) previewCard.IsVisible = false;
            }
            catch (Exception ex) { Debug.WriteLine($"[Generate] ClearForm: {ex}"); }
        }

        private void ResetFieldStyles()
        {
            var def = (Style)Resources["EntryContainerStyle"];
            fullNameFrame.Style = def;
            EmailFrame.Style = def;
            phoneFrame.Style = def;
            categoryFrame.Style = def;
            amountFrame.Style = def;
        }

        private void HideAllValidationMessages()
        {
            fullNameError.IsVisible = false;
            EmailError.IsVisible = false;
            phoneError.IsVisible = false;
            categoryError.IsVisible = false;
            amountError.IsVisible = false;
            amountFormatted.IsVisible = false;

            fullNameValidIcon.IsVisible = false;
            EmailValidIcon.IsVisible = false;
            phoneValidIcon.IsVisible = false;
            categoryValidIcon.IsVisible = false;
            amountValidIcon.IsVisible = false;
        }

        private static string ExtractReferenceNumber(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message ?? string.Empty;

            // Primary: "Your Payment Reference is 311452476016"
            var anchor = Regex.Match(message,
                @"(?:reference|invoice)\s+(?:number\s+)?(?:is\s+)?(\d{6,})",
                RegexOptions.IgnoreCase);
            if (anchor.Success) return anchor.Groups[1].Value;

            // Fallback: longest digit sequence in the message
            var matches = Regex.Matches(message, @"\d+");
            if (matches.Count == 0) return message;
            return matches.Cast<System.Text.RegularExpressions.Match>()
                          .OrderByDescending(m => m.Length)
                          .First().Value;
        }
        // ─────────────────────────────────────────────────────────────────────
        //  INPC
        // ─────────────────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(
            [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DATA MODELS
    // ══════════════════════════════════════════════════════════════════════════

    public class PaymentData
    {
        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("serviceTypeId")]
        public string ServiceTypeId { get; set; }

        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }
    }

    public class InvoiceRequest
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string PaymentItem { get; set; }
        public decimal Amount { get; set; }
    }

    public class InvoiceResult
    {
        public string message { get; set; }
        public string status_code { get; set; }
        public string InvoiceId { get; set; }
        public string ReferenceNumber { get; set; }
        public string PaymentUrl { get; set; }
        public string Status { get; set; }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorDetails { get; set; }
    }

    public class ErrorResponse
    {
        public string message { get; set; }
        public string error { get; set; }
        public string status_code { get; set; }
        public string details { get; set; }
    }
}