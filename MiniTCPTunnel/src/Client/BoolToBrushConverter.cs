using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MiniTCPTunnel.Client;

// bool 값을 색상 브러시로 변환하는 변환기:
// - true  -> 연결됨(녹색 계열)
// - false -> 연결 중/끊김(주황 계열)
public sealed class BoolToBrushConverter : IValueConverter
{
    // true일 때 사용할 브러시(기본값을 지정해 XAML에서 쉽게 덮어쓴다).
    public IBrush TrueBrush { get; set; } = Brushes.LimeGreen;

    // false일 때 사용할 브러시(기본값을 지정해 XAML에서 쉽게 덮어쓴다).
    public IBrush FalseBrush { get; set; } = Brushes.Orange;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 바인딩 값이 bool이면 그에 맞는 브러시를 돌려준다.
        if (value is bool flag)
        {
            return flag ? TrueBrush : FalseBrush;
        }

        // 타입이 맞지 않으면 안전하게 false 색상을 반환한다.
        return FalseBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // UI에서 브러시를 bool로 역변환할 일은 없으므로 명시적으로 막는다.
        throw new NotSupportedException("BoolToBrushConverter는 ConvertBack을 지원하지 않습니다.");
    }
}
