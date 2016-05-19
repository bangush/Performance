// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Test.Perf.WebFx.Apps.LowAlloc
{
    public class PooledHttpContextFactory : IHttpContextFactory
    {
        private static readonly int _poolCount = CalculatePoolCount();
        private static readonly int _poolMask = _poolCount - 1;

        private CacheLinePadded<int> _rentPoolIndex = new CacheLinePadded<int>();
        private CacheLinePadded<int> _returnPoolIndex = new CacheLinePadded<int>();
        private ComponentPool<PooledHttpContext>[] _pools = new ComponentPool<PooledHttpContext>[_poolCount];

        private int _maxPooled;
        private int _maxPerPool;

        public int MaxPooled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _maxPooled; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxPooled));
                if (value != _maxPooled)
                {
                    var maxPerPool = (int)Math.Ceiling(value / (double)_poolCount);
                    if (maxPerPool == 0 && value > 0)
                    {
                        maxPerPool = 1;
                    }

                    Interlocked.Exchange(ref _pools, CreatePools(_poolCount, maxPerPool));

                    _maxPooled = value;
                    _maxPerPool = maxPerPool;
                }
            }
        }

        public PooledHttpContextFactory(IConfiguration configuration)
        {
            MaxPooled = GetPooledCount(configuration["hosting.maxPooledContexts"]);
        }

        public HttpContext Create(IFeatureCollection featureCollection)
        {
            if (_maxPooled == 0)
            {
                return new PooledHttpContext(featureCollection);
            }

            PooledHttpContext httpContext = null;

            if (RentPool.TryRent(out httpContext))
            {
                httpContext.Initialize(featureCollection);
            }
            else
            {
                httpContext = new PooledHttpContext(featureCollection);
            }

            return httpContext;
        }

        public void Dispose(HttpContext httpContext)
        {
            if (MaxPooled > 0)
            {
                var pooled = httpContext as PooledHttpContext;
                if (pooled != null)
                {
                    pooled.Uninitialize();
                    ReturnPool.Return(pooled);
                }
            }
        }

        private static int GetPooledCount(string countString)
        {
            if (string.IsNullOrEmpty(countString))
            {
                return 0;
            }

            int count;
            if (int.TryParse(countString, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                return count > 0 ? count : 0;
            }

            return 0;
        }

        private ComponentPool<PooledHttpContext> RentPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Volatile.Read(ref _pools[Interlocked.Increment(ref _rentPoolIndex.Value) & _poolMask]);
            }
        }

        private ComponentPool<PooledHttpContext> ReturnPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Volatile.Read(ref _pools[Interlocked.Increment(ref _returnPoolIndex.Value) & _poolMask]);
            }
        }

        private static ComponentPool<PooledHttpContext>[] CreatePools(int poolCount, int maxPerPool)
        {
            var pools = new ComponentPool<PooledHttpContext>[poolCount];
            for (var i = 0; i < pools.Length; i++)
            {
                pools[i] = new ComponentPool<PooledHttpContext>(maxPerPool);
            }

            return pools;
        }

        private static int CalculatePoolCount()
        {
            var processors = Environment.ProcessorCount;

            if (processors > 64) return 256;
            if (processors > 32) return 128;
            if (processors > 16) return 64;
            if (processors > 8) return 32;
            if (processors > 4) return 16;
            if (processors > 2) return 8;
            if (processors > 1) return 4;
            return 2;
        }
    }
}
