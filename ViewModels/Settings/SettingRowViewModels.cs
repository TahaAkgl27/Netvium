using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Netvium.Models;

namespace Netvium.ViewModels.Settings
{
    public enum SettingRowKind
    {
        Toggle,
        Choice,
        Text,
        Password,
        Number,
        Slider,
        Info,
        Action,
        LicenseActivation
    }

    public sealed class SettingsNavItem
    {
        private string _title = string.Empty;
        public SettingsCategory Category { get; init; }
        public string Title
        {
            get => _title;
            init => _title = SettingsTextCatalog.Common(value);
        }
        public string IconGlyph { get; init; } = "\uE713";
    }

    public abstract class SettingRowViewModel : ViewModelBase
    {
        protected SettingRowViewModel(string key, string title, string description, SettingsCategory category, params string[] searchTags)
        {
            var localized = SettingsTextCatalog.Row(key, title, description);
            Key = key;
            Title = localized.Title;
            Description = localized.Description;
            Category = category;
            SearchBlob = $"{title} {description} {Title} {Description} {key} {string.Join(' ', searchTags)}".ToLowerInvariant();
        }

        public string Key { get; }
        public string Title { get; }
        public string Description { get; }
        public SettingsCategory Category { get; }
        public string SearchBlob { get; }
        public abstract SettingRowKind Kind { get; }
        public event EventHandler? ValueChanged;

        protected void RaiseValueChanged() => ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public sealed class BoolSettingRowViewModel : SettingRowViewModel
    {
        private bool _value;

        public BoolSettingRowViewModel(string key, string title, string description, SettingsCategory category, bool initial, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            _value = initial;
        }

        public override SettingRowKind Kind => SettingRowKind.Toggle;

        public bool Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                    RaiseValueChanged();
            }
        }
    }

    public sealed class ChoiceSettingRowViewModel : SettingRowViewModel
    {
        private string _selectedValue = string.Empty;
        private readonly Dictionary<string, string> _canonicalValues = new(StringComparer.Ordinal);

        public ChoiceSettingRowViewModel(string key, string title, string description, SettingsCategory category,
            IEnumerable<string> choices, string initial, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            Choices = new ObservableCollection<string>();
            foreach (var choice in choices)
            {
                var displayValue = SettingsTextCatalog.Common(choice);
                Choices.Add(displayValue);
                _canonicalValues[displayValue] = choice;
            }

            _selectedValue = SettingsTextCatalog.Common(initial);
        }

        public override SettingRowKind Kind => SettingRowKind.Choice;
        public ObservableCollection<string> Choices { get; }
        public string CanonicalValue =>
            _canonicalValues.TryGetValue(_selectedValue, out var canonical) ? canonical : _selectedValue;

        public string SelectedValue
        {
            get => _selectedValue;
            set
            {
                if (SetProperty(ref _selectedValue, value))
                    RaiseValueChanged();
            }
        }
    }

    public sealed class TextSettingRowViewModel : SettingRowViewModel
    {
        private string _value = string.Empty;

        public TextSettingRowViewModel(string key, string title, string description, SettingsCategory category, string initial, bool isPassword = false, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            _value = initial;
            IsPassword = isPassword;
        }

        public override SettingRowKind Kind => IsPassword ? SettingRowKind.Password : SettingRowKind.Text;
        public bool IsPassword { get; }

        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                    RaiseValueChanged();
            }
        }
    }

    public sealed class NumberSettingRowViewModel : SettingRowViewModel
    {
        private int _value;

        public NumberSettingRowViewModel(string key, string title, string description, SettingsCategory category,
            int initial, int min, int max, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            _value = initial;
            Min = min;
            Max = max;
        }

        public override SettingRowKind Kind => SettingRowKind.Number;
        public int Min { get; }
        public int Max { get; }

        public int Value
        {
            get => _value;
            set
            {
                var clamped = Math.Clamp(value, Min, Max);
                if (SetProperty(ref _value, clamped))
                    RaiseValueChanged();
            }
        }
    }

    public sealed class SliderSettingRowViewModel : SettingRowViewModel
    {
        private double _value;

        public SliderSettingRowViewModel(string key, string title, string description, SettingsCategory category,
            double initial, double min, double max, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            _value = initial;
            Min = min;
            Max = max;
        }

        public override SettingRowKind Kind => SettingRowKind.Slider;

        public double Min { get; }
        public double Max { get; }

        public double Value
        {
            get => _value;
            set
            {
                var clamped = Math.Clamp(value, Min, Max);
                if (SetProperty(ref _value, clamped))
                    RaiseValueChanged();
            }
        }
    }

    public sealed class InfoSettingRowViewModel : SettingRowViewModel
    {
        public InfoSettingRowViewModel(string key, string title, string description, SettingsCategory category, string value, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            Value = value;
        }

        public override SettingRowKind Kind => SettingRowKind.Info;
        public string Value { get; }
    }

    public sealed class ActionSettingRowViewModel : SettingRowViewModel
    {
        public ActionSettingRowViewModel(string key, string title, string description, SettingsCategory category,
            string buttonText, ICommand command, bool isDestructive = false, params string[] searchTags)
            : base(key, title, description, category, searchTags)
        {
            ButtonText = SettingsTextCatalog.Common(buttonText);
            Command = command;
            IsDestructive = isDestructive;
        }

        public override SettingRowKind Kind => SettingRowKind.Action;
        public string ButtonText { get; }
        public ICommand Command { get; }
        public bool IsDestructive { get; }
    }

    public sealed class LicenseActivationSettingRowViewModel : SettingRowViewModel
    {
        private string _activationCode = string.Empty;
        private string _statusText;
        private string _feedbackText = string.Empty;

        public LicenseActivationSettingRowViewModel(string statusText)
            : base("licenseActivation", "License", "Activate Premium+ with your offline activation code.", SettingsCategory.License, "premium plus activation license")
        {
            _statusText = statusText;
        }

        public override SettingRowKind Kind => SettingRowKind.LicenseActivation;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string ActivationCode
        {
            get => _activationCode;
            set => SetProperty(ref _activationCode, value);
        }

        public string FeedbackText
        {
            get => _feedbackText;
            set => SetProperty(ref _feedbackText, value);
        }

        public ICommand? ActivateCommand { get; set; }
    }
}
