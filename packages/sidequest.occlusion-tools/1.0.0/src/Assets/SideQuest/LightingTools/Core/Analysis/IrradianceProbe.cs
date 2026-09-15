// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// A deliberately cheap estimate of how much light reaches a point, and how fast that
    /// changes nearby.
    ///
    /// This is not a renderer and does not try to be. Probe density only needs to know
    /// where lighting varies sharply - a doorway edge, a shadow boundary, the falloff of a
    /// lamp - so that probes cluster there and thin out across a uniformly lit floor.
    /// A full bake would answer the same question far more accurately, and far too slowly
    /// to run while someone is dragging a slider.
    /// </summary>
    public sealed class IrradianceProbe
    {
        readonly List<Light> _lights = new List<Light>();
        readonly bool _useShadowRays;

        public IrradianceProbe(IList<Light> lights, bool useShadowRays = true)
        {
            _useShadowRays = useShadowRays;
            if (lights == null) return;

            for (int i = 0; i < lights.Count; i++)
            {
                Light light = lights[i];
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
                if (light.intensity <= 0f) continue;
                _lights.Add(light);
            }
        }

        public int LightCount { get { return _lights.Count; } }

        /// <summary>Scalar estimate of incident light at a point. Arbitrary units; only ratios matter.</summary>
        public float Estimate(Vector3 point)
        {
            float total = 0f;

            for (int i = 0; i < _lights.Count; i++)
            {
                Light light = _lights[i];

                switch (light.type)
                {
                    case LightType.Directional:
                        total += light.intensity * Visibility(point, point - light.transform.forward * 50f, 50f);
                        break;

                    case LightType.Point:
                    case LightType.Spot:
                    {
                        Vector3 lightPosition = light.transform.position;
                        float distance = Vector3.Distance(point, lightPosition);
                        if (distance > light.range) break;

                        // Unity's real falloff is more involved; inverse-square clamped to
                        // range is close enough to rank one position against another.
                        float attenuation = 1f / Mathf.Max(distance * distance, 0.25f);
                        float rangeFade = 1f - Mathf.Clamp01(distance / Mathf.Max(light.range, 0.001f));

                        if (light.type == LightType.Spot)
                        {
                            Vector3 toPoint = (point - lightPosition).normalized;
                            float angle = Vector3.Angle(light.transform.forward, toPoint);
                            if (angle > light.spotAngle * 0.5f) break;
                        }

                        total += light.intensity * attenuation * rangeFade * Visibility(point, lightPosition, distance);
                        break;
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// One shadow ray per light. This is the expensive part, so it is optional and the
        /// caller turns it off for a first-pass sweep over thousands of candidate cells.
        /// </summary>
        float Visibility(Vector3 point, Vector3 lightPosition, float distance)
        {
            if (!_useShadowRays) return 1f;

            Vector3 direction = lightPosition - point;
            if (direction.sqrMagnitude < 0.0001f) return 1f;

            return Physics.Raycast(point, direction.normalized, distance - 0.05f, ~0, QueryTriggerInteraction.Ignore)
                ? 0f
                : 1f;
        }

        /// <summary>
        /// How sharply lighting changes around a point, sampled on a small cross.
        ///
        /// Normalised by the mean so a dim corner and a bright hall are ranked on relative
        /// contrast rather than absolute brightness - otherwise every probe would pile up
        /// around the brightest lamp and none would land on the shadow edge that actually
        /// needs them.
        /// </summary>
        public float GradientScore(Vector3 point, float sampleRadius)
        {
            float centre = Estimate(point);

            float sum = centre;
            float sumSquares = centre * centre;
            int samples = 1;

            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 offset = Vector3.zero;
                offset[axis] = sampleRadius;

                float a = Estimate(point + offset);
                float b = Estimate(point - offset);

                sum += a + b;
                sumSquares += a * a + b * b;
                samples += 2;
            }

            float mean = sum / samples;
            if (mean <= 0.0001f) return 0f;

            float variance = Mathf.Max(0f, sumSquares / samples - mean * mean);
            return Mathf.Sqrt(variance) / mean;
        }
    }
}
