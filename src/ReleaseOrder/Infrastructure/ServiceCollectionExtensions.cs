using Microsoft.Extensions.DependencyInjection;
using Contracts.Services;
using Contracts.Repositories;
using ReleaseOrderDemo.Services;
using ReleaseOrderDemo.Activities;

namespace ReleaseOrderDemo.Infrastructure
{
    /// <summary>
    /// Extensiones de IServiceCollection para registrar todos los servicios del worker ReleaseOrder.
    /// OCP: agregar nuevas implementaciones no requiere modificar Program.cs.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddReleaseOrderServices(
            this IServiceCollection services,
            string connectionString)
        {
            // Repositorios — Transient: abren una SqlConnection nueva por llamada
            services.AddTransient<IOrderRepository>(_ => new OrderRepository(connectionString));
            services.AddTransient<IProductRepository>(_ => new ProductRepository(connectionString));
            services.AddTransient<IShipmentRepository>(_ => new ShipmentRepository(connectionString));

            // Servicios
            // PaymentService es Singleton: mantiene _processedPayments en memoria entre actividades
            services.AddSingleton<IPaymentService, PaymentService>();
            services.AddTransient<IInventoryService, InventoryService>();
            services.AddTransient<IShippingService, ShippingService>();

            // Actividades — Transient: Temporal resuelve una instancia por ejecución
            services.AddTransient<InventoryActivities>();
            services.AddTransient<PaymentActivities>();
            services.AddTransient<ShippingActivities>();
            services.AddTransient<OrderStatusActivities>();
            services.AddTransient<OrderLookupActivities>();

            return services;
        }
    }
}
