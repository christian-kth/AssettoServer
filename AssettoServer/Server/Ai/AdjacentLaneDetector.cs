﻿using System;
using System.Numerics;
using System.Threading.Tasks;
using Serilog;
using SerilogTimings;

namespace AssettoServer.Server.Ai
{
    public static class AdjacentLaneDetector
    {
        private static Vector3 OffsetVec(Vector3 pos, float angle, float offset)
        {
            return new()
            {
                X = (float) (pos.X + Math.Cos(angle * Math.PI / 180) * offset),
                Y = pos.Y,
                Z = (float) (pos.Z - Math.Sin(angle * Math.PI / 180) * offset)
            };
        }

        public static void DetectAdjacentLanes(TrafficMap map, float laneWidth)
        {
            Log.Information("Adjacent lane detection...");
            
            using var t = Operation.Time("Adjacent lane detection");

            foreach (var spline in map.Splines)
            {
                Parallel.ForEach(spline.Points, point =>
                {
                    if (point.Right == null && point.Next != null)
                    {
                        float direction = (float) (Math.Atan2(point.Point.Z - point.Next.Point.Z, point.Next.Point.X - point.Point.X) * (180 / Math.PI) * -1);

                        var targetVec = OffsetVec(point.Point, -direction + 90, laneWidth);

                        var found = map.WorldToSpline(targetVec);

                        if (found.distanceSquared < 2 * 2)
                        {
                            point.Left = found.point;
                            if (point.IsSameDirection(found.point))
                            {
                                found.point.Right = point;
                            }
                            else
                            {
                                found.point.Left = point;
                            }
                        }
                        
                        targetVec = OffsetVec(point.Point, -direction - 90, laneWidth);

                        found = map.WorldToSpline(targetVec);

                        if (found.distanceSquared < 2 * 2)
                        {
                            point.Right = found.point;
                            if (point.IsSameDirection(found.point))
                            {
                                found.point.Left = point;
                            }
                            else
                            {
                                found.point.Right = point;
                            }
                        }
                    }
                });
            }
        }
    }
}