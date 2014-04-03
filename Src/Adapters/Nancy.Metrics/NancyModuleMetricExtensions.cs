﻿using System;
using System.IO;
using Metrics;
using Nancy.Routing;

namespace Nancy.Metrics
{
    public static class NancyModuleMetricExtensions
    {
        public static void MetricForRequestTimeAndSize(this INancyModule module, string metricName, string method, string pathPrefix)
        {
            module.MetricForRequestTimeAndSize(metricName, module.MakePredicate(method, pathPrefix));
        }

        public static void MetricForRequestTimeAndSize(this INancyModule module, string metricName, Predicate<RouteDescription> routePredicate)
        {
            module.MetricForRequestTime(metricName + ".Time", routePredicate);
            module.MetricForRequestSize(metricName + ".Size", routePredicate);
        }

        public static void MetricForRequestTime(this INancyModule module, string metricName, string method, string pathPrefix)
        {
            module.MetricForRequestTime(metricName, module.MakePredicate(method, pathPrefix));
        }

        public static void MetricForRequestTime(this INancyModule module, string metricName, Predicate<RouteDescription> routePredicate)
        {
            var name = string.Format("{0}.{1}", module.GetType().Name, metricName);
            CheckNancyMetricsIsConfigured();
            var timer = NancyMetrics.CurrentConfig.Registry.Timer(name, Unit.Requests, SamplingType.FavourRecent, TimeUnit.Seconds, TimeUnit.Milliseconds);
            var key = "Metrics.Nancy.Request.Timer." + metricName;

            module.Before.AddItemToStartOfPipeline(ctx =>
            {
                if (routePredicate(ctx.ResolvedRoute.Description))
                {
                    ctx.Items[key] = timer.NewContext();
                }
                return null;
            });

            module.After.AddItemToEndOfPipeline(ctx =>
            {
                if (routePredicate(ctx.ResolvedRoute.Description))
                {
                    using (ctx.Items[key] as IDisposable) { }
                    ctx.Items.Remove(key);
                }
            });
        }

        public static void MetricForRequestSize(this INancyModule module, string metricName, string method, string pathPrefix)
        {
            module.MetricForRequestSize(metricName, module.MakePredicate(method, pathPrefix));
        }

        public static void MetricForRequestSize(this INancyModule module, string metricName, Predicate<RouteDescription> routePredicate)
        {
            var name = string.Format("{0}.{1}", module.GetType().Name, metricName);
            CheckNancyMetricsIsConfigured();
            var histogram = NancyMetrics.CurrentConfig.Registry.Histogram(name, Unit.Custom("bytes"), SamplingType.FavourRecent);

            module.After.AddItemToEndOfPipeline(ctx =>
            {
                if (routePredicate(ctx.ResolvedRoute.Description))
                {
                    string lengthHeader;
                    // if available use content length header
                    if (ctx.Response.Headers.TryGetValue("Content-Length", out lengthHeader))
                    {
                        long length;
                        if (long.TryParse(lengthHeader, out length))
                        {
                            histogram.Update(length);
                        }
                    }
                    else
                    {
                        // if no content length - get the length of the stream
                        // this might be suboptimal for some types of requests
                        using (var ns = new NullStream())
                        {
                            ctx.Response.Contents(ns);
                            histogram.Update(ns.Length);
                        }
                    }
                }
            });
        }

        private static void CheckNancyMetricsIsConfigured()
        {
            if (NancyMetrics.CurrentConfig == null)
            {
                NancyMetrics.Configure();
            }
        }

        private static Predicate<RouteDescription> MakePredicate(this INancyModule module, string methodName, string pathPrefix)
        {
            if (string.IsNullOrEmpty(pathPrefix) || !pathPrefix.StartsWith("/"))
            {
                throw new ArgumentException("pathPrefix must start with / ", pathPrefix);
            }

            var path = (module.ModulePath + pathPrefix).ToUpper();

            return d => (string.IsNullOrEmpty(methodName) || methodName.ToUpper() == "ANY" || d.Method.ToUpper() == methodName.ToUpper())
                && d.Path.ToUpper().StartsWith(path);
        }

        /// <summary>
        /// Fake stream used for getting the response size.
        /// This class is stolen from NancyFx sources.
        /// https://github.com/NancyFx/Nancy/blob/master/src/Nancy/HeadResponse.cs#L45
        /// </summary>
        private sealed class NullStream : Stream
        {
            private int bytesWritten;

            public override long Length { get { return this.bytesWritten; } }

            public override void Write(byte[] buffer, int offset, int count)
            {
                // We assume we can't seek and can't overwrite, but don't throw just in case.
                this.bytesWritten += count;
            }

            public override void Flush() { }
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) { throw new NotSupportedException(); }
            public override int EndRead(IAsyncResult asyncResult) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override int ReadByte() { throw new NotSupportedException(); }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanTimeout { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        }
    }
}