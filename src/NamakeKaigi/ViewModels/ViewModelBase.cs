using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NamakeKaigi.ViewModels;

/// <summary>ViewModelの共通の土台(プロパティ変更の通知)。</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>プロパティの変更を画面へ知らせる。</summary>
    /// <param name="name">プロパティ名(省略時は呼び出し元)</param>
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>値が変わったときだけ書き換えて、変更を通知する。</summary>
    /// <param name="field">保持している値</param>
    /// <param name="value">新しい値</param>
    /// <param name="name">プロパティ名(省略時は呼び出し元)</param>
    /// <returns>変わったらtrue</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>ボタンなどから呼ぶ、引数なしのコマンド。</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>コマンドを作る。</summary>
    /// <param name="execute">実行する処理</param>
    /// <param name="canExecute">実行できるかの判定(省略時は常に可)</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>実行できるかの判定が変わったことを知らせる。</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
