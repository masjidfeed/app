namespace MasjidApp.Views;

/// <summary>Displays a photo over the current page.</summary>
public partial class ImagePreviewPage : ContentPage
{
    private const double MaxPhotoScale = 5;
    private double _pinchStartScale = 1;
    private Point _panStart;

    internal ImagePreviewPage(ImageSource source)
    {
        InitializeComponent();
        PreviewImage.Source = source;
    }

    private void OnPhotoPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _pinchStartScale = PreviewImage.Scale;
        }

        if (e.Status != GestureStatus.Running)
        {
            return;
        }

        double previousScale = PreviewImage.Scale;
        double scale = Math.Clamp(previousScale + (e.Scale - 1) * _pinchStartScale, 1, MaxPhotoScale);
        double scaleChange = 1 - scale / previousScale;
        double originX = (e.ScaleOrigin.X - 0.5) * PhotoViewport.Width;
        double originY = (e.ScaleOrigin.Y - 0.5) * PhotoViewport.Height;
        double x = PreviewImage.TranslationX;
        double y = PreviewImage.TranslationY;

        // Keep the point under the pinch stationary as the image scales around its center.
        SetPhotoTransform(scale, x + (originX - x) * scaleChange, y + (originY - y) * scaleChange);
    }

    private void OnPhotoPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStart = new Point(PreviewImage.TranslationX, PreviewImage.TranslationY);
                break;
            case GestureStatus.Running when PreviewImage.Scale > 1:
                SetPhotoTransform(PreviewImage.Scale, _panStart.X + e.TotalX, _panStart.Y + e.TotalY);
                break;
        }
    }

    private void SetPhotoTransform(double scale, double x, double y)
    {
        double maxX = PhotoViewport.Width * (scale - 1) / 2;
        double maxY = PhotoViewport.Height * (scale - 1) / 2;
        PreviewImage.Scale = scale;
        PreviewImage.TranslationX = Math.Clamp(x, -maxX, maxX);
        PreviewImage.TranslationY = Math.Clamp(y, -maxY, maxY);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        CloseButton.IsEnabled = false;
        await Navigation.PopModalAsync(animated: false);
    }
}
