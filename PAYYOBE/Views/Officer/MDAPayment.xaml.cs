using Acr.UserDialogs;
using Newtonsoft.Json;
using PAYYOBE.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace PAYYOBE.Views.Officer
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MDAPayment : ContentPage
    {
        private readonly HttpClient _http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true }) { Timeout = TimeSpan.FromSeconds(30) };
        private OfficerTransaction _verifiedCachedTransaction;

        public MDAPayment()
        {
            InitializeComponent();
        }




        private async Task PresentInteractiveOverlayAsync(Xamarin.Forms.PancakeView.PancakeView activeSheet)
        {
            try
            {
                if (SheetOverlay == null || activeSheet == null) return;

                SheetOverlay.IsVisible = true;
                activeSheet.IsVisible = true;
                activeSheet.TranslationY = 1000;
                await activeSheet.TranslateTo(0, 0, 300, Easing.Linear);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDAInvoice] Sheet Presentation Exception: {ex.Message}");
            }
        }

        private void OnRrrTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(e.NewTextValue))
                {
                    RecordDetailsCard.IsVisible = false;
                    return;
                }

                // Real-time alphanumeric code string cleaning
                string structuralDigits = new string(e.NewTextValue.Where(char.IsDigit).ToArray());

                // Show the payment option immediately when 12 digits are reached
                if (structuralDigits.Length == 12)
                {
                    TxtRrr.Unfocus(); // Dismiss keyboard
                    RecordDetailsCard.IsVisible = true;
                }
                else
                {
                    RecordDetailsCard.IsVisible = false;
                }
            }
            catch { }
        }

        private async void OnPostPaymentClicked(object sender, EventArgs e)
        {
            string rrrCode = TxtRrr.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rrrCode)) return;

            SetPostLoadingState(true);

            try
            {
                string targetUrl = "https://payyobe.com/api/v1/MakePayment";

                // Build the payload directly from the input entry instead of a cached model
                var bodyParametersPayload = new { OfficerId = MainPage.OfficerId, RRR = rrrCode };

                string contentPayloadSerialized = JsonConvert.SerializeObject(bodyParametersPayload);
                using (var abstractStringContent = new StringContent(contentPayloadSerialized, Encoding.UTF8, "application/json"))
                {
                    var response = await _http.PostAsync(targetUrl, abstractStringContent);
                    if (!response.IsSuccessStatusCode)
                    {
                        await DisplayAlert("Gateway Intercept", $"Settlement router rejected processing instance: {(int)response.StatusCode}", "OK");
                        return;
                    }

                    string outcomeJsonRaw = await response.Content.ReadAsStringAsync();
                    var executionModelResult = JsonConvert.DeserializeObject<MdaCollectionPaymentResult>(outcomeJsonRaw);

                    if (executionModelResult != null && executionModelResult.statusCode == "00")
                    {
                        // Bind mapping values falling back securely if needed
                        LblSheetPayer.Text = executionModelResult.payer ?? "Officer Collection Step";
                        LblSheetRrr.Text = executionModelResult.rrr ?? rrrCode;
                        LblSheetAmount.Text = $"₦{executionModelResult.amount:N0}";

                        // Present receipt confirmation sheet overlay view
                        await PresentInteractiveOverlayAsync(SuccessSheet);
                    }
                    else
                    {
                        await DisplayAlert("Execution Refusal", executionModelResult?.message ?? "The bank clearing network returned an unhandled transaction fault loop parameter.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Critical Core Fault", $"Authorization Loop Interrupt Error Context: {ex.Message}", "OK");
            }
            finally
            {
                SetPostLoadingState(false);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  THERMAL HARDWARE CORE DISPATCH ENGAGEMENTS
        // ─────────────────────────────────────────────────────────────────

       
        private async void OnPrintSlipClicked(object sender, EventArgs e)
        {
            // Use the input field text directly since verification is skipped
            string rrrCode = TxtRrr.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rrrCode)) return;

            try
            {
                using (UserDialogs.Instance.Loading("Spooling Thermal Invoice Data..."))
                {
                    var itemCollectionMatrix = new List<ReceiptItem>
                    {
                        new ReceiptItem { Description = "REMITA  RRR", Amount = 0m, SubText = _verifiedCachedTransaction.rrr },
                        new ReceiptItem { Description = "Payer Name", Amount = 0m, SubText = LblSheetPayer.Text },
                        new ReceiptItem { Description = "Agent Name", Amount = 0m, SubText = $"Officer ID #{MainPage.OfficerName}" },
                        new ReceiptItem { Description = "Amount Settled", Amount = (decimal)_verifiedCachedTransaction.amount }
                    };

                    var standardReceiptDataContract = new ReceiptData
                    {
                        StoreName = "YOBE STATE REVENUE SERVICES [YIRS]",
                        StorePhone = "Contact: +234 803 052 3208",
                        ReceiptNumber = _verifiedCachedTransaction.rrr,
                        AgentName = MainPage.OfficerName,
                        PrintDate = DateTime.Now,
                        Items = itemCollectionMatrix,
                        AmountPaid = (decimal)_verifiedCachedTransaction.amount,
                        FooterLine1 = "This payment confirmation document is legally verified.",
                        FooterLine2 = "POWERED BY OSOFTPAY "
                    };

                    // FIX: Instantiating service safely to bypass App.PrintJobManager null pointers
                    var printService = new BluetoothPrinterService(use80mm: false);

                    // Dispatch task directly via your platform-bound peripheral engine hook
                    await printService.PrintReceiptAsync(standardReceiptDataContract, "Logo.png", "YOBE PAY", default(System.Threading.CancellationToken));

                    UserDialogs.Instance.Toast("Receipt job dispatched to printer queue.");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Printing Error", $"Thermal link reported an operational hardware error sequence: {ex.Message}", "OK");
            }
        }

        private void SetPostLoadingState(bool executing)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                PostLoadingIndicator.IsVisible = executing;
                PostLoadingIndicator.IsRunning = executing;
                LblPostBtn.Text = executing ? "CLEARING ACCOUNT SETTLE BALANCE..." : "CONFIRM & PROCESS PAYMENT";
                BtnPostPayment.IsEnabled = !executing;
            });
        }

        private async void OnDismissSheetClicked(object sender, EventArgs e) => await ClearOverlaySheetsAsync();
        private async void OnOverlayDismissTapped(object sender, EventArgs e) => await ClearOverlaySheetsAsync();

        private async Task ClearOverlaySheetsAsync()
        {
            try
            {
                await SuccessSheet.TranslateTo(0, 1000, 250, Easing.Linear);
                SuccessSheet.IsVisible = false;
                SheetOverlay.IsVisible = false;
                RecordDetailsCard.IsVisible = false;
                TxtRrr.Text = string.Empty;
            }
            catch { }
        }

        private async void OnBackClicked(object sender, EventArgs e) => await Navigation.PopAsync();
    }

    // ─────────────────────────────────────────────────────────────────
    //  STRONG-TYPED INTEGRATION APIS PACKAGING CONTRACT MODALS
    // ─────────────────────────────────────────────────────────────────

    public class MdaCollectionPaymentResult
    {
        public string statusCode { get; set; }
        public string message { get; set; }
        public string rrr { get; set; }
        public double amount { get; set; }
        public string payer { get; set; }
    }
}