using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace StormRetroArch;

public enum SettingControlKind
{
    Toggle,
    ComboBox,
    Slider,
    TextBox,
    DirectoryPath
}

/// <summary>
/// Represents a single real RetroArch configuration option with metadata,
/// two-way binding, and instant synchronization with retroarch.cfg.
/// </summary>
public class SettingItem : INotifyPropertyChanged
{
    private bool _boolVal;
    private string _stringVal = "";
    private double _numVal;
    private string _formattedNum = "";

    public string CategoryId { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string CategoryIcon { get; set; } = "⚙️";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CfgKey { get; set; } = "";
    public SettingControlKind Kind { get; set; } = SettingControlKind.Toggle;

    public List<string> Options { get; set; } = new();
    public double Min { get; set; } = 0;
    public double Max { get; set; } = 100;
    public double Step { get; set; } = 1;
    public string Unit { get; set; } = "";

    public Visibility ToggleVisibility => Kind == SettingControlKind.Toggle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ComboBoxVisibility => Kind == SettingControlKind.ComboBox ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SliderVisibility => Kind == SettingControlKind.Slider ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextBoxVisibility => Kind == SettingControlKind.TextBox ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DirectoryVisibility => Kind == SettingControlKind.DirectoryPath ? Visibility.Visible : Visibility.Collapsed;

    public bool BoolValue
    {
        get => _boolVal;
        set
        {
            if (SetField(ref _boolVal, value))
            {
                RetroArchConfigManager.SetBool(CfgKey, value);
            }
        }
    }

    public string StringValue
    {
        get => _stringVal;
        set
        {
            if (SetField(ref _stringVal, value))
            {
                RetroArchConfigManager.SetString(CfgKey, value);
            }
        }
    }

    public double NumericValue
    {
        get => _numVal;
        set
        {
            if (SetField(ref _numVal, value))
            {
                FormattedNumeric = FormatNumeric(value);
                if (Step < 1)
                    RetroArchConfigManager.SetFloat(CfgKey, (float)value);
                else
                    RetroArchConfigManager.SetInt(CfgKey, (int)value);
            }
        }
    }

    public string FormattedNumeric
    {
        get => _formattedNum;
        set => SetField(ref _formattedNum, value);
    }

    private string FormatNumeric(double v)
    {
        if (!string.IsNullOrEmpty(Unit))
        {
            if (Step < 1) return $"{v:F2} {Unit}";
            return $"{v:F0} {Unit}";
        }
        if (Step < 1) return $"{v:F2}";
        return $"{v:F0}";
    }

    public void RefreshFromConfig()
    {
        switch (Kind)
        {
            case SettingControlKind.Toggle:
                _boolVal = RetroArchConfigManager.GetBool(CfgKey, false);
                OnPropertyChanged(nameof(BoolValue));
                break;
            case SettingControlKind.ComboBox:
            case SettingControlKind.TextBox:
            case SettingControlKind.DirectoryPath:
                _stringVal = RetroArchConfigManager.GetString(CfgKey, "");
                OnPropertyChanged(nameof(StringValue));
                break;
            case SettingControlKind.Slider:
                if (Step < 1)
                    _numVal = RetroArchConfigManager.GetFloat(CfgKey, 0f);
                else
                    _numVal = RetroArchConfigManager.GetInt(CfgKey, 0);
                _formattedNum = FormatNumeric(_numVal);
                OnPropertyChanged(nameof(NumericValue));
                OnPropertyChanged(nameof(FormattedNumeric));
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
