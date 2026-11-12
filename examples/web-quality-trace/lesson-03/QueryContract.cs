using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

static class QueryContract
{
    public static void AddQueryContract(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.ShouldInclude = description => (description.HttpMethod == "GET" &&
                description.RelativePath is "api/inspections" or "api/inspections/{id}" or "api/inspections/{id}/image" or "api/inspections/{id}/evidence" or "api/inspection-events" or "api/inspection-pages") ||
                description.RelativePath is "api/session" or "api/session/login" or "api/session/logout" or "api/inspections/{id}/review";
            options.AddDocumentTransformer((document, context, ct) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["localSession"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = "QualityTrace.Session",
                    Description = "Issued by POST /api/session/login after CSRF validation."
                };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, ct) =>
            {
                if (context.Description.RelativePath == "api/inspections")
                {
                    operation.Description = "Filters use import time in UTC. since is inclusive; until is exclusive. "
                        + "format=csv exports all matching rows, up to 1000, ignoring page/pageSize slicing. "
                        + "reviewStatus is optional: pending or reviewed selects NG by saved review existence; requires Review:Enabled.";
                    operation.Responses!["200"].Content!["text/csv"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    };
                }
                if (context.Description.RelativePath is "api/inspections" or "api/inspection-pages")
                {
                    var response = (OpenApiResponse)operation.Responses!["429"];
                    response.Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["Retry-After"] = new OpenApiHeader
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            Description = "Suggested wait in whole seconds when opt-in read limiting is enabled."
                        }
                    };
                }
                if (context.Description.RelativePath == "api/inspections/{id}/image")
                {
                    operation.Description = "BMP preview. Revalidate a cached copy with If-None-Match.";
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "If-None-Match", In = ParameterLocation.Header,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        Description = "Returns 304 without a body when the current ETag matches."
                    });
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "If-Match", In = ParameterLocation.Header,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        Description = "Requires a matching strong ETag or *, otherwise returns 412."
                    });
                    var success = (OpenApiResponse)operation.Responses!["200"];
                    success.Content ??= new Dictionary<string, OpenApiMediaType>();
                    success.Content["image/bmp"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                    };
                    foreach (string code in new[] { "200", "304" })
                    {
                        var response = (OpenApiResponse)operation.Responses[code];
                        response.Headers = new Dictionary<string, IOpenApiHeader>
                        {
                            ["ETag"] = new OpenApiHeader
                            {
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                                Description = "Strong validator of the returned BMP bytes."
                            },
                            ["Cache-Control"] = new OpenApiHeader
                            {
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                                Description = "private, no-cache"
                            }
                        };
                    }
                }
                if (context.Description.RelativePath == "api/inspections/{id}/evidence")
                {
                    operation.Description = "Original PGM attachment. The stored SHA-256 is checked before download.";
                    var success = (OpenApiResponse)operation.Responses!["200"];
                    success.Content ??= new Dictionary<string, OpenApiMediaType>();
                    success.Content["application/octet-stream"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                    };
                    success.Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["Content-Disposition"] = new OpenApiHeader
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            Description = "Attachment with the inspection ID as the .pgm filename."
                        },
                        ["Content-Digest"] = new OpenApiHeader
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            Description = "SHA-256 of the complete PGM, encoded as a structured byte sequence."
                        },
                        ["Cache-Control"] = new OpenApiHeader
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            Description = "no-store"
                        }
                    };
                }
                if (context.Description.RelativePath == "api/inspection-pages")
                {
                    operation.Description = "Newest first. station and result (OK or NG) are required. "
                        + "since is inclusive and until is exclusive in UTC. pageSize defaults to 20, range 1..100. "
                        + "Pass nextCursor as after with the same filters, including reviewStatus. A null nextCursor ends the current traversal. "
                        + "reviewStatus is optional: pending or reviewed selects NG by saved review existence; requires Review:Enabled.";
                }
                if (context.Description.RelativePath == "api/inspection-events")
                {
                    operation.Description = "Loopback-only event feed. after defaults to decimal string 0, "
                        + "must be in 0..9223372036854775807; limit defaults to 20 and allows 1..100. "
                        + "Sequence is strictly greater than after. A cursor ahead of the stream returns 409. "
                        + "Compare streamId before consuming. Empty items preserve the input cursor. "
                        + "hasMore=false means caught up to this request's captured head.";
                }
                if (context.Description.RelativePath == "api/inspections/{id}/review" && context.Description.HttpMethod == "POST")
                {
                    var media = operation.Responses!["400"].Content!["application/problem+json"];
                    media.Schema = new OpenApiSchema
                    {
                        AllOf = [media.Schema!],
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["errors"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                AdditionalProperties = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            }
                        }
                    };
                }
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("localSession", context.Document)] = []
                    });
                }
                if (context.Description.HttpMethod == "POST")
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "X-CSRF-Token", In = ParameterLocation.Header, Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        Description = "Use csrf from GET /api/session with its matching antiforgery cookie. Refresh after login/logout."
                    });
                }
                return Task.CompletedTask;
            });
        });
    }

    public static void MapQueryContract(this WebApplication app)
    {
        app.MapOpenApi().AddEndpointFilter(async (context, next) =>
            context.HttpContext.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address)
                ? await next(context) : Results.StatusCode(403));
    }
}
