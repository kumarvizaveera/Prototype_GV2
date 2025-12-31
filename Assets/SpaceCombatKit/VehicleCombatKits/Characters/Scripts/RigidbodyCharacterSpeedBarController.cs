using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VSX.UI;

namespace VSX.Characters
{
    public class RigidbodyCharacterSpeedBarController : MonoBehaviour
    {

        public Image speedBarFill;

        [SerializeField]
        protected TextController speedText;

        public RigidbodyCharacterController characterController;

        public Rigidbody m_Rigidbody;


        private void Update()
        {
            speedBarFill.fillAmount = m_Rigidbody.linearVelocity.magnitude / characterController.RunSpeed;
            if (speedText != null) speedText.text = ((int)m_Rigidbody.linearVelocity.magnitude).ToString();
        }
    }

}
