using UnityEngine;
using System.Collections;
using System.Collections.Generic;


[RequireComponent(typeof(Animator))]


public class Weapons : MonoBehaviour {

	// ─── Tag constants ───────────────────────────────────────────────────────
	private const string TAG_DIRT   = "Dirt";
	private const string TAG_METAL  = "Metal";
	private const string TAG_WOOD   = "Wood";
	private const string TAG_GLASS  = "Glass";
	private const string TAG_WATER  = "Water";
	private const string TAG_BLOOD  = "Blood";
	private const string TAG_GROUND = "Ground";
	private const string TAG_ENEMY  = "Enemy";

	public AudioClip WeaponEmpty;

	[System.Serializable]
	public class WeaponsSetup{


		public Transform WeapObj;
		public Transform leftHandle;
		public Transform rightHandle;
		public Texture2D crossTexture;
		public GameObject MuzzleFlash;
		public AudioClip WeaponSound;
		public string WAnimation = "None";
		public string WIk = "Bazooka.Bazooka";
		public string PreFireAnim;
		public string FireAnim;
		public int RaysPerShoot;
		public int Bullets;
		public int Magazine;
		public int MaxBulletInMagazine;
		public AudioSource ReloadSound;
		public float FireRate;
		public float FireAnimatorSpeed;
		public float Power = 0.0f;
		public float Imprecision;
		public float DamageValue;

		//RigidBodyBullete (grenade&Rifle)
		public Rigidbody RigidBodyPrefab;
		public Transform RigidSpawnTarget;

	}


	public enum BaseState
	{
		Base,
		Combat,
		Climb,
		Swim,
		Pracute,
		JetPack

	}

	public BaseState AnimState = BaseState.Base;

	public AnimControllers animCtrl;
	[System.Serializable]
	public class AnimControllers
	{
		public RuntimeAnimatorController Base;
		//public RuntimeAnimatorController Combat;
		//public RuntimeAnimatorController Climb;
		//public RuntimeAnimatorController Swim;
		//public RuntimeAnimatorController Parachute;
		//public RuntimeAnimatorController Jetpack;
	}
	public WeaponsSetup[] weaponsSetup;
	private Animator m_Animator;
	private bool Reloading;
	
	public GameObject[] _hitParticles;
	public float HitParticlesLifeTime = 10.1f;

	public int weaponIndex = 0;
	public int OldWeapIndex;

	

	public float ReloadTime = 1.0f;
	private float LastReload = 0.0f;
	private float nextFire = 0.0f;
	//Other Weapons
	private RaycastHit hit;
	private Ray ray;
	//Grenade
	private int PowerThrow;
	public GameObject HitMarker;
	public AudioSource ReloadSound;

	// ─── Cached references (avoid per-frame FindObject / GetComponent) ───────
	private AudioSource _audioSource;
	private Camera _mainCamera;
	private OrbitCamera _camScript;

	// ─── Per-tag hit-particle pools ──────────────────────────────────────────
	// Index matches _hitParticles array: 0=Dirt,1=Metal,2=Wood,3=Glass,4=Water,5=Blood,6=Ground
	private ObjectPool[] _hitParticlePools;
	private Transform _poolRoot;

	// ─── HitMarker pool ──────────────────────────────────────────────────────
	private ObjectPool _hitMarkerPool;

	void Start () 
	{
		m_Animator   = GetComponent<Animator>();
		_audioSource = GetComponent<AudioSource>();
		_mainCamera  = Camera.main;

		// Cache the orbit-camera script once
		GameObject camObj = GameObject.Find("PlayerCamera");
		if (camObj != null)
			_camScript = camObj.GetComponent<OrbitCamera>();

		// Build a pool root to keep the hierarchy tidy
		_poolRoot = new GameObject("WeaponPools").transform;
		_poolRoot.SetParent(transform, false);

		// Pre-warm pools for each hit-particle prefab
		_hitParticlePools = new ObjectPool[_hitParticles.Length];
		for (int i = 0; i < _hitParticles.Length; i++)
		{
			if (_hitParticles[i] != null)
				_hitParticlePools[i] = new ObjectPool(_hitParticles[i], 5, _poolRoot);
		}

		// Pre-warm hit-marker pool
		if (HitMarker != null)
			_hitMarkerPool = new ObjectPool(HitMarker, 3, _poolRoot);
	}

	/// <summary>
	/// Retrieves a hit-particle from the pool, positions it, and schedules its return.
	/// </summary>
	private void SpawnHitParticle(int index, Vector3 point, Quaternion rotation, Transform parent)
	{
		if (index < 0 || index >= _hitParticlePools.Length || _hitParticlePools[index] == null)
			return;

		ObjectPool pool = _hitParticlePools[index];
		GameObject obj  = pool.Get(point, rotation);
		obj.transform.SetParent(parent, true);
		StartCoroutine(ReturnToPool(pool, obj, HitParticlesLifeTime));
	}

	private static IEnumerator ReturnToPool(ObjectPool pool, GameObject obj, float delay)
	{
		yield return new WaitForSeconds(delay);
		if (obj != null)
			pool.Return(obj);
	}

	void Update()
	{
		var fwd = transform.TransformDirection(Vector3.forward);

		//Setting Key
		bool Shoot = Input.GetButton ("Fire1");
		bool Aim = Input.GetButton ("Fire2");
		bool Reload = Input.GetKey (KeyCode.R);
		bool ThrowRelease = Input.GetButtonUp ("Fire1");

		// Cache current weapon setup to avoid repeated array indexing
		WeaponsSetup ws = weaponsSetup[weaponIndex];

		if (ws.FireAnim != "") {
			m_Animator.SetBool (ws.FireAnim, false);
				}

		if (Reload && ws.Magazine > 0 && ws.Bullets >= 0 && !Aim) {
						Reloading = true;
						m_Animator.SetBool ("Reload", true);
			ReloadSound.Play();

			ws.Bullets = ws.MaxBulletInMagazine;
			ws.Magazine = ws.Magazine - 1;


			LastReload = Time.time + ReloadTime;

			} else {

			if (Time.time > LastReload){
			Reloading = false;
			m_Animator.SetBool ("Reload", false);	
			}
		}


		if (Shoot && Aim && ws.WAnimation == "Equip" && Time.time > nextFire && ws.Bullets > 0 && !Reloading) {

			m_Animator.SetBool (ws.FireAnim, true);
			m_Animator.speed = ws.FireAnimatorSpeed;

			ws.MuzzleFlash.SetActive (true);
			_audioSource.PlayOneShot(ws.WeaponSound);
			ws.Bullets = ws.Bullets - 1;

			nextFire = Time.time + ws.FireRate;

			float halfW = Screen.width  * 0.5f;
			float halfH = Screen.height * 0.5f;

			for(int i = 0; i < ws.RaysPerShoot; i++){
				// Regular raycast using vec and transform.position

				Vector2 screenCenterPoint = new Vector2 (
					halfW + Random.Range( ws.Imprecision, -ws.Imprecision),
					halfH + Random.Range( ws.Imprecision, -ws.Imprecision));
				
				ray = _mainCamera.ScreenPointToRay (screenCenterPoint);

				if (Physics.Raycast (ray, out hit, _mainCamera.farClipPlane)) {

					Quaternion rot = Quaternion.FromToRotation (Vector3.forward, hit.normal);
					Collider col  = hit.collider;

					if      (col.CompareTag(TAG_DIRT))   SpawnHitParticle(0, hit.point, rot, hit.transform);
					else if (col.CompareTag(TAG_METAL))  SpawnHitParticle(1, hit.point, rot, hit.transform);
					else if (col.CompareTag(TAG_WOOD))   SpawnHitParticle(2, hit.point, rot, hit.transform);
					else if (col.CompareTag(TAG_GLASS))  SpawnHitParticle(3, hit.point, rot, hit.transform);
					else if (col.CompareTag(TAG_WATER))  SpawnHitParticle(4, hit.point, rot, hit.transform);
					else if (col.CompareTag(TAG_BLOOD))  SpawnHitParticle(5, hit.point, rot, hit.transform);
					else if (col.CompareTag(TAG_GROUND)) SpawnHitParticle(6, hit.point, rot, hit.transform);

					if (col.CompareTag(TAG_ENEMY))
					{
						SpawnHitParticle(5, hit.point, rot, hit.transform);
						col.GetComponent<Health>().CurrentHealth -= ws.DamageValue;
						if (_hitMarkerPool != null)
						{
							GameObject marker = _hitMarkerPool.Get(Vector3.zero, Quaternion.identity);
							StartCoroutine(ReturnToPool(_hitMarkerPool, marker, 0.2f));
						}
					}
					if (hit.rigidbody)
						
						hit.rigidbody.AddForceAtPosition (fwd * ws.Power, hit.point);  //applies a force to a rigidbody
						}
		}	

			
		} else {


			if (ws.MuzzleFlash && Time.time > nextFire) {

				ws.MuzzleFlash.SetActive (false);
				m_Animator.speed = 0.91f;


						}
				}
		//End 

		//grenade & bomb Launch parameters

		if (Shoot && ws.WAnimation == "Throw") {

			m_Animator.SetBool ("PreThrow", true);	
			PowerThrow = PowerThrow + 20; // Power Launch increment
			if (PowerThrow > 800)
				PowerThrow = 800;
		}

		if (ThrowRelease && ws.WAnimation == "Throw" && Time.time > nextFire && ws.Bullets > 0) {
			m_Animator.SetBool ("Throw", true);
			m_Animator.SetBool ("PreThrow", false);	
			Rigidbody GranadeInstance;
			GranadeInstance = Instantiate(ws.RigidBodyPrefab, ws.RigidSpawnTarget.position, ws.RigidSpawnTarget.rotation) as Rigidbody;
			GranadeInstance.AddForce(ws.RigidSpawnTarget.forward * PowerThrow);
			ws.Bullets = ws.Bullets - 1;
			PowerThrow = 0;
			if(ws.Bullets == 0)
				weaponIndex = 0;
	
		}
		else
		{
			
			m_Animator.SetBool("Throw" , false);                
		}
		//End

		//Bazooka Parameters
		if(Shoot && Aim && ws.WAnimation == "Bazooka" && Time.time > nextFire && ws.Bullets > 0)
		{
			nextFire = Time.time + ws.FireRate;
			ws.Bullets = ws.Bullets - 1;

			Rigidbody rocketInstance;
			rocketInstance = Instantiate(ws.RigidBodyPrefab, ws.RigidSpawnTarget.position, ws.RigidSpawnTarget.rotation) as Rigidbody;
			rocketInstance.AddForce(ws.RigidSpawnTarget.forward * 5000);

		}
		if ( Input.GetButtonDown("Fire1") && ws.Bullets == 0 && ws.WAnimation == "Equip"){
			_audioSource.PlayOneShot(WeaponEmpty);
		}
	}
	//End


	void LateUpdate () 
	{
		// Use cached camera script — avoids GameObject.Find every LateUpdate
		if (_camScript != null)
			_camScript.crosshairTexture = weaponsSetup[weaponIndex].crossTexture;

		WeaponsSetup ws    = weaponsSetup[weaponIndex];
		WeaponsSetup oldWs = weaponsSetup[OldWeapIndex];

	//	if (weaponIndex != 0) {

			ws.WeapObj.tag = "on";
			ws.WeapObj.GetComponent<Renderer>().enabled = true;

	//		if (OldWeapIndex!=0){
			oldWs.WeapObj.GetComponent<Renderer>().enabled = false;
			oldWs.WeapObj.tag = "off";
//			}
	//			}

		if (ws.WeapObj) {

			if (ws.WeapObj.CompareTag("on")) {

				if (Input.GetButton ("Fire2") && ws.WAnimation != "Bazooka" && !Reloading) {

					m_Animator.SetBool (ws.PreFireAnim, true);


										//WIk = "Armed.PreFire";
				
				} else {
					if (ws.WAnimation != "Bazooka")
					m_Animator.SetBool (ws.PreFireAnim, false);
					m_Animator.SetBool (ws.WAnimation, true);

								}

						}


			if (oldWs.WeapObj.CompareTag("off")) {

				if (OldWeapIndex != 0){
				oldWs.leftHandle  = null;
				oldWs.rightHandle = null;
				}
				if (oldWs.WAnimation != ws.WAnimation) //Only if animation is different
				m_Animator.SetBool (oldWs.WAnimation, false);

						}

						m_Animator.SetLayerWeight (2, 1);


			}
		}


	void OnAnimatorIK(int layerIndex)
	{
		if(!enabled) return;
		
		if (layerIndex == 2) // do the log holding on the last layer, since LookAt is done in previous layer
		{
			WeaponsSetup ws = weaponsSetup[weaponIndex];
			float ikWeight  = m_Animator.GetCurrentAnimatorStateInfo(2).IsName(ws.WIk) ? 1 : 0;
			
			if (ws.leftHandle != null)
			{
				m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, ws.leftHandle.transform.position);
				m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, ws.leftHandle.transform.rotation);
				m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, ikWeight);
				m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, ikWeight);
			}
			
			if (ws.rightHandle != null)
			{
				m_Animator.SetIKPosition(AvatarIKGoal.RightHand, ws.rightHandle.transform.position);
				m_Animator.SetIKRotation(AvatarIKGoal.RightHand, ws.rightHandle.transform.rotation);
				m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, ikWeight);
				m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, ikWeight);
			}
		}
	}
}
