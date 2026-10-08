using System.Windows;

namespace KerioTrafficMonitor.Presentation.Views;

public partial class UserDialog : Window
{
    public string Username => UsernameBox.Text.Trim();
    public string Password => PasswordBox.Password;

    public UserDialog()
    {
        InitializeComponent();
        UsernameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            MessageBox.Show(this, "Введите логин.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            MessageBox.Show(this, "Введите пароль.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
