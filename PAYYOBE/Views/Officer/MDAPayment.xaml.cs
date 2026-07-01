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

        private void OnRrrTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(e.NewTextValue)) return;

                // Real-time alphanumeric code string cleaning
                string structuralDigits = new string(e.NewTextValue.Where(char.IsDigit).ToArray());
              
            }
            catch { }
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

        // ─────────────────────────────────────────────────────────────────
        //  STEP 1: QUERY TRANSACTION BY ID & RRR CRITERIA (GET WORKFLOW)
        // ─────────────────────────────────────────────────────────────────

  

        // ─────────────────────────────────────────────────────────────────
        //  STEP 2: POST MAKE PAYMENT WORKFLOW PROCESSING
        // ─────────────────────────────────────────────────────────────────

        private async void OnPostPaymentClicked(object sender, EventArgs e)
        {
            if (_verifiedCachedTransaction == null) return;

            SetPostLoadingState(true);

            try
            {
                string targetUrl = "https://payyobe.com/api/v1/MakePayment";
                var bodyParametersPayload = new { OfficerId = MainPage.OfficerId, RRR = _verifiedCachedTransaction.rrr };

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
                        // Bind mapping values safely to the display success container module sheets
                        LblSheetPayer.Text = executionModelResult.payer ?? _verifiedCachedTransaction.payer_Name;
                        LblSheetRrr.Text = executionModelResult.rrr;
                        LblSheetAmount.Text = $"₦{executionModelResult.amount:N0}";

                        // Present receipt confirmation node view screen elements layout sheets
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
            if (_verifiedCachedTransaction == null) return;

            try
            {
                using (UserDialogs.Instance.Loading("Spooling Thermal Invoice Data..."))
                {
                    var itemCollectionMatrix = new List<ReceiptItem>
                    {
                        new ReceiptItem { Description = "REMITA COLLECTION CODE RRR", Amount = 0m, SubText = _verifiedCachedTransaction.rrr },
                        new ReceiptItem { Description = "Allocated Revenue Service", Amount = 0m, SubText = _verifiedCachedTransaction.service_Name },
                        new ReceiptItem { Description = "Payer Depositor Name", Amount = 0m, SubText = LblSheetPayer.Text },
                        new ReceiptItem { Description = "Account Settlement Node ID", Amount = 0m, SubText = $"Officer ID #{MainPage.OfficerId}" },
                        new ReceiptItem { Description = "Total Value Settled", Amount = (decimal)_verifiedCachedTransaction.amount }
                    };

                    var standardReceiptDataContract = new ReceiptData
                    {
                        StoreName = "YOBE STATE REVENUE SERVICES [YIRS]",
                        StorePhone = "Official Revenue Collector Copy Terminal",
                        ReceiptNumber = _verifiedCachedTransaction.rrr,
                        AgentName = MainPage.OfficerName,
                        CollectionPoint = "YIRS Officer Operational Desk",
                        PrintDate = DateTime.Now,
                        Items = itemCollectionMatrix,
                        AmountPaid = (decimal)_verifiedCachedTransaction.amount,
                        FooterLine1 = "This payment confirmation document is legally verified.",
                        FooterLine2 = "SYSTEM INFRASTRUCTURE NET POWERED BY OSOFTPAY"
                    };

                    var job = await App.PrintJobManager.EnqueueAsync(standardReceiptDataContract, "Logo.png");
                    var timeoutCancellationToken = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(40));

                    await App.PrintJobManager.ExecuteAsync(job.JobId, new Progress<PrintProgress>(), timeoutCancellationToken.Token);
                    UserDialogs.Instance.Toast("Receipt job dispatched to printer queue.");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Printing Error", $"Thermal link reported an operational hardware error sequence: {ex.Message}", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  UI STATE UTILITY MANAGEMENT ENGINES
        // ─────────────────────────────────────────────────────────────────

 

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