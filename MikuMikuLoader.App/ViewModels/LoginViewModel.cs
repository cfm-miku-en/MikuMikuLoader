using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuMikuLoader.App.Services;

namespace MikuMikuLoader.App.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly Action _onSignedIn;

    public LoginViewModel(ApiClient api, SessionService session, Action onSignedIn)
    {
        _api = api;
        _session = session;
        _onSignedIn = onSignedIn;
    }

    [ObservableProperty] private bool _isRegister;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _asDeveloper;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    public string Title => IsRegister ? "Create an account" : "Sign in";
    public string SubmitText => IsRegister ? "Create account" : "Sign in";
    public string ToggleText => IsRegister ? "Have an account? Sign in" : "Need an account? Create one";

    partial void OnIsRegisterChanged(bool value)
    {
        Error = "";
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(ToggleText));
    }

    [RelayCommand]
    private void ToggleMode() => IsRegister = !IsRegister;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        Error = "";
        var user = (Username ?? "").Trim();
        var pass = Password ?? "";
        if (user.Length < 3) { Error = "Username must be at least 3 characters."; return; }
        if (pass.Length < 8) { Error = "Password must be at least 8 characters."; return; }

        IsBusy = true;
        try
        {
            var auth = IsRegister
                ? await _api.RegisterAsync(user, pass, AsDeveloper)
                : await _api.LoginAsync(user, pass);

            _session.SignIn(auth);
            Password = "";
            _onSignedIn();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
