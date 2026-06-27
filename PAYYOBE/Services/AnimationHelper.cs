using System;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace PAYYOBE.Services
{
    /// <summary>
    /// Animation helper class
    /// </summary>
    public static class AnimationHelper
    {
        public static async Task PulseAnimation(View view, uint duration = 300)
        {
            try
            {
                await view.ScaleTo(1.1, duration / 2, Easing.CubicOut);
                await view.ScaleTo(1, duration / 2, Easing.CubicIn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pulse animation error: {ex.Message}");
            }
        }

        public static async Task ShakeAnimation(View view, uint duration = 300)
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    await view.TranslateTo(-10, 0, duration / 6);
                    await view.TranslateTo(10, 0, duration / 6);
                }
                await view.TranslateTo(0, 0, duration / 6);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shake animation error: {ex.Message}");
            }
        }

        public static async Task SlideInFromLeft(View view, uint duration = 500)
        {
            try
            {
                view.TranslationX = -view.Width;
                view.Opacity = 0;

                var translateTask = view.TranslateTo(0, 0, duration, Easing.CubicOut);
                var fadeTask = view.FadeTo(1, duration, Easing.Linear);

                await Task.WhenAll(translateTask, fadeTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slide in animation error: {ex.Message}");
            }
        }

        public static async Task SlideInFromRight(View view, uint duration = 500)
        {
            try
            {
                view.TranslationX = view.Width;
                view.Opacity = 0;

                var translateTask = view.TranslateTo(0, 0, duration, Easing.CubicOut);
                var fadeTask = view.FadeTo(1, duration, Easing.Linear);

                await Task.WhenAll(translateTask, fadeTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slide in animation error: {ex.Message}");
            }
        }

        public static async Task FadeInUp(View view, uint duration = 600)
        {
            try
            {
                view.TranslationY = 50;
                view.Opacity = 0;

                var translateTask = view.TranslateTo(0, 0, duration, Easing.CubicOut);
                var fadeTask = view.FadeTo(1, duration, Easing.Linear);

                await Task.WhenAll(translateTask, fadeTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fade in up animation error: {ex.Message}");
            }
        }

        public static async Task SuccessBounce(View view, uint duration = 400)
        {
            try
            {
                await view.ScaleTo(1.2, duration / 2, Easing.CubicOut);
                await view.ScaleTo(1, duration / 2, Easing.BounceOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Success bounce animation error: {ex.Message}");
            }
        }
    }

}
