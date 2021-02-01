using System;
using UnityEngine;
using UnityEngine.Experimental.Perception.Randomization.Parameters;
using UnityEngine.Experimental.Perception.Randomization.Randomizers.SampleRandomizers.Tags;
using UnityEngine.Experimental.Perception.Randomization.Samplers;

namespace UnityEngine.Experimental.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Chooses a random of frame of a random clip for a game object
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Animation Randomizer")]
    public class AnimationRandomizer : Randomizer
    {
        FloatParameter m_FloatParameter = new FloatParameter{ value = new UniformSampler(0, 1) };

        const string k_ClipName = "PlayerIdle";
        const string k_StateName = "Base Layer.RandomState";

        void RandomizeAnimation(AnimationRandomizerTag tag)
        {
            var animator = tag.gameObject.GetComponent<Animator>();
            animator.applyRootMotion = tag.applyRootMotion;

            var overrider = tag.animatorOverrideController;
            if (overrider != null && tag.animationClips.GetCategoryCount() > 0)
            {
                overrider[k_ClipName] = tag.animationClips.Sample();
                animator.Play(k_StateName, 0, m_FloatParameter.Sample());
            }
        }

        /// <inheritdoc/>
        protected override void OnIterationStart()
        {
            if (m_FloatParameter == null) m_FloatParameter = new FloatParameter{ value = new UniformSampler(0, 1) };

            var taggedObjects = tagManager.Query<AnimationRandomizerTag>();
            foreach (var taggedObject in taggedObjects)
            {
                RandomizeAnimation(taggedObject);
            }
        }
    }
}