using System.Text.Json.Serialization;

namespace CesiumAI.Api.Models;

/// <summary>
/// 相机场景动作；JSON 使用小写字符串枚举值。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CameraAction>))]
public enum CameraAction
{
    [JsonStringEnumMemberName("focus")]
    Focus,

    [JsonStringEnumMemberName("track")]
    Track,

    [JsonStringEnumMemberName("untrack")]
    Untrack,

    [JsonStringEnumMemberName("zoom")]
    Zoom,

    [JsonStringEnumMemberName("pan")]
    Pan,

    [JsonStringEnumMemberName("rotate")]
    Rotate,

    [JsonStringEnumMemberName("orbitStep")]
    OrbitStep,

    [JsonStringEnumMemberName("orbitStart")]
    OrbitStart,

    [JsonStringEnumMemberName("orbitStop")]
    OrbitStop
}
