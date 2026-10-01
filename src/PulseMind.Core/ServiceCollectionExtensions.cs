using Microsoft.Extensions.DependencyInjection;
using PulseMind.Core.Devices;
using PulseMind.Core.Health;
using PulseMind.Core.Records;
using PulseMind.Core.Study;

namespace PulseMind.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>アプリの中心となる処理を登録する（データベースの設定は呼び出し側で行う）</summary>
    public static IServiceCollection AddPulseMindCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<StudySessionService>();
        services.AddScoped<DailyRecordService>();
        services.AddScoped<HeartRateIngestionService>();
        services.AddScoped<DeviceService>();
        return services;
    }
}
