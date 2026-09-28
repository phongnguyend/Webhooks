using WebhookRouter.Endpoints;
using WebhookRouter.Services;

namespace WebhookRouter;

public static class DependencyInjection
{
    public static WebApplication MapEndpoints(this WebApplication app, bool microsoftEnabled)
    {
        app.MapPasswordSignIn();
        app.MapActivityLog();
        if (microsoftEnabled)
        {
            app.MapMicrosoftSignIn();
        }

        app.MapGoogleSignIn();
        app.MapHealthEndpoints();
        app.MapEventEndpoints();
        app.MapProfileEndpoints();
        app.MapUserEndpoints();
        app.MapTenantEndpoints();
        app.MapTopicEndpoints();
        app.MapWebhookEndpoints();
        return app;
    }
}
