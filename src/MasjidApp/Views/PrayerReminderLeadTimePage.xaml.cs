using MasjidApp.ViewModels;

namespace MasjidApp.Views;

public partial class PrayerReminderLeadTimePage : ContentPage
{
    public PrayerReminderLeadTimePage(PrayerReminderLeadTimeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
