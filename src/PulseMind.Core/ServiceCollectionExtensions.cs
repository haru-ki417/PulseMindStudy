using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseMind.Core.Devices;
using PulseMind.Core.Health;
using PulseMind.Core.Insights;
using PulseMind.Core.Privacy;
using PulseMind.Core.Profiles;
using PulseMind.Core.Records;
using PulseMind.Core.Study;

namespace PulseMind.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>アプリの中心となる処理を登録する（データベースの設定は呼び出し側で行う）</summary>
    public static IServiceCollection AddPulseMindCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StudySessionService>();
        services.AddScoped<DailyRecordService>();
        services.AddScoped<HeartRateIngestionService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<UserProfileService>();
        services.AddScoped<InsightService>();
        services.AddScoped<AuditLog>();
        services.AddScoped<DataExportService>();
        return services;
    }
}
