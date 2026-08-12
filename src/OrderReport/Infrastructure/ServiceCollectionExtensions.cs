using Microsoft.Extensions.DependencyInjection;
using Contracts.Services;
using OrderReportDemo.Services;
using OrderReportDemo.Activities;

namespace OrderReportDemo.Infrastructure
{
    /// <summary>
    /// Extensiones de IServiceCollection para registrar todos los servicios del worker OrderReport.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOrderReportServices(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddTransient<IReportService>(_ => new ReportService(connectionString));
            services.AddTransient<OrderReportActivities>();

            return services;
        }
    }
}
