using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public static class AnimationUtils
{
    public static int FadeOutVisualElement(GameObject gameObject, VisualElement visualElement, float animTimeInSeconds)
    {
        return LeanTween
            .value(gameObject, visualElement.resolvedStyle.opacity, 0, animTimeInSeconds)
            .setOnUpdate(interpolatedValue => visualElement.style.opacity = interpolatedValue)
            .id;
    }

    public static int FadeInVisualElement(GameObject gameObject, VisualElement visualElement, float animTimeInSeconds)
    {
        return LeanTween
            .value(gameObject, visualElement.resolvedStyle.opacity, 1, animTimeInSeconds)
            .setOnUpdate(interpolatedValue => visualElement.style.opacity = interpolatedValue)
            .id;
    }

    public static int HighlightIconWithBounce(GameObject gameObject, VisualElement visualElement)
    {
        visualElement.style.scale = Vector2.zero;
        return LeanTween.value(gameObject, 0, 1, 1f)
            .setOnUpdate(value =>
            {
                visualElement.style.scale = new Vector2(value, value);
            })
            .setEaseSpring()
            .id;
    }

    public static int BounceVisualElementSize(GameObject gameObject, VisualElement visualElement, float animTimeInSeconds, float delayInSeconds = 0)
    {
        return LeanTween.value(gameObject, Vector3.one, Vector3.one * 0.75f, animTimeInSeconds)
            .setOnUpdate(s => visualElement.style.scale = new StyleScale(new Scale(new Vector2(s, s))))
            .setDelay(delayInSeconds)
            .setEasePunch()
            .id;
    }

    public static async Awaitable FadeOutThenRemoveVisualElementAsync(
        VisualElement visualElement,
        float solidTimeInSeconds,
        float fadeOutTimeInSeconds)
    {
        await Awaitable.WaitForSecondsAsync(solidTimeInSeconds);

        float startOpacity = visualElement.resolvedStyle.opacity;
        float startTime = Time.time;
        while (visualElement.resolvedStyle.opacity > 0)
        {
            float newOpacity = Mathf.Lerp(startOpacity, 0, (Time.time - startTime) / fadeOutTimeInSeconds);
            if (newOpacity < 0)
            {
                newOpacity = 0;
            }

            visualElement.style.opacity = newOpacity;
            await Awaitable.NextFrameAsync();
        }

        // Remove VisualElement
        if (visualElement.parent != null)
        {
            visualElement.parent.Remove(visualElement);
        }
    }

    public static async Awaitable TransitionBackgroundImageGradientAsync(VisualElement visualElement,
        GradientConfig fromGradientConfig, GradientConfig toGradientConfig, float animTimeInSeconds)
    {
        List<GradientConfig> gradientConfigs = GradientManager.GetGradientConfigsForTransition(fromGradientConfig, toGradientConfig, animTimeInSeconds);
        await TransitionBackgroundImageGradientAsync(visualElement, gradientConfigs, animTimeInSeconds);
    }

    private static async Awaitable TransitionBackgroundImageGradientAsync(VisualElement visualElement,
        List<GradientConfig> gradientConfigs, float animTimeInSeconds)
    {
        await Awaitable.MainThreadAsync();

        foreach (GradientConfig gradientConfig in gradientConfigs)
        {
            visualElement.style.backgroundImage = GradientManager.GetGradientTexture(gradientConfig);
            await Awaitable.WaitForSecondsAsync(animTimeInSeconds / gradientConfigs.Count);
        }
    }
}
