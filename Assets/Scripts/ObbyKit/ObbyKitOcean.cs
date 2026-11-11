// =====================================================================================
//  ObbyKitOcean.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  Lives on the animated ocean. Plays a splash of droplets wherever the player falls
//  into the water. The water surface is this object's Y position.
//
//  UNITY SETUP
//  The environment builder creates this for you. To set it up yourself, put it on the
//  ocean mesh (material ObbyKit/CartoonWater) and assign a Splash Material that uses
//  ObbyKit/SplashParticle. The VoidZone underneath still does the respawn: keep its top
//  a little below the water so the splash shows first.
// =====================================================================================

using UnityEngine;

namespace ObbyKit
{
    [DisallowMultipleComponent]
    public class ObbyKitOcean : MonoBehaviour
    {
        [SerializeField] private Material splashMaterial;
        [SerializeField] private Color splashColor = new Color(0.9f, 0.97f, 1f, 0.95f);
        [SerializeField] private int dropletCount = 45;

        private PlayerController player;
        private ParticleSystem splash;
        private float previousY;
        private bool hasPrevious;

        /// <summary>World height of the water surface.</summary>
        public float WaterLevel => transform.position.y;

        private void Start()
        {
            player = FindObjectOfType<PlayerController>();
            BuildSplash();
        }

        private void BuildSplash()
        {
            var go = new GameObject("Splash");
            go.transform.SetParent(transform, false);
            splash = go.AddComponent<ParticleSystem>();
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = splash.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.65f);
            main.startColor = splashColor;
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = splash.emission;
            emission.enabled = false;

            var shape = splash.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.7f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // spray upwards

            var size = splash.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (splashMaterial != null) renderer.sharedMaterial = splashMaterial;
        }

        private void Update()
        {
            if (player == null) { player = FindObjectOfType<PlayerController>(); return; }
            float y = player.transform.position.y;
            if (hasPrevious && previousY > WaterLevel && y <= WaterLevel)
                Splash(new Vector3(player.transform.position.x, WaterLevel + 0.1f, player.transform.position.z));
            previousY = y;
            hasPrevious = true;
        }

        /// <summary>Spray droplets at a point on the surface.</summary>
        public void Splash(Vector3 position)
        {
            if (splash == null) return;
            splash.transform.position = position;
            splash.Emit(dropletCount);
        }
    }
}
