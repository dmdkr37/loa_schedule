using System.Windows;
using System.Windows.Controls;

namespace LoaSchedule.App.Behaviors;

public static class PasswordBoxBinding
{
    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating",
        typeof(bool),
        typeof(PasswordBoxBinding));

    public static readonly DependencyProperty BindPasswordProperty = DependencyProperty.RegisterAttached(
        "BindPassword",
        typeof(bool),
        typeof(PasswordBoxBinding),
        new PropertyMetadata(false, OnBindPasswordChanged));

    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static bool GetBindPassword(DependencyObject target) =>
        (bool)target.GetValue(BindPasswordProperty);

    public static void SetBindPassword(DependencyObject target, bool value) =>
        target.SetValue(BindPasswordProperty, value);

    public static string GetBoundPassword(DependencyObject target) =>
        (string)target.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject target, string value) =>
        target.SetValue(BoundPasswordProperty, value);

    private static void OnBindPasswordChanged(DependencyObject target, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (target is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= OnPasswordChanged;
        if (eventArgs.NewValue is true)
        {
            passwordBox.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject target, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (target is not PasswordBox passwordBox || (bool)passwordBox.GetValue(IsUpdatingProperty))
        {
            return;
        }

        passwordBox.Password = eventArgs.NewValue as string ?? string.Empty;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        var passwordBox = (PasswordBox)sender;
        passwordBox.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        passwordBox.SetValue(IsUpdatingProperty, false);
    }
}
