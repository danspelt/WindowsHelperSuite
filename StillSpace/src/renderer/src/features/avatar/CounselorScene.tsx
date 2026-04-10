import { useFrame } from '@react-three/fiber'
import { useMemo, useRef } from 'react'
import { Group, Mesh } from 'three'
import { RoomScene } from '../../three/scenes/RoomScene'

type Props = {
  listening: boolean
  speaking: boolean
}

export function CounselorScene({ listening, speaking }: Props) {
  const group = useRef<Group>(null)
  const breath = useRef(0)

  const chairColor = useMemo(() => '#3d3530', [])

  useFrame((_s, dt) => {
    breath.current += dt
    if (group.current) {
      const b = Math.sin(breath.current * 1.1) * 0.006
      group.current.position.y = b
      const nod = listening ? Math.sin(breath.current * 2.4) * 0.02 : 0
      group.current.rotation.x = nod
    }
  })

  return (
    <>
      <RoomScene />
      <group ref={group} position={[0, 0, 0]}>
        {/* Seated counselor — stylized, not hyper-real */}
        <mesh position={[0, 0.35, 0.2]} castShadow receiveShadow>
          <boxGeometry args={[0.95, 0.12, 0.95]} />
          <meshStandardMaterial color={chairColor} roughness={0.85} />
        </mesh>
        <mesh position={[0, 0.75, 0.15]} castShadow>
          <boxGeometry args={[0.55, 0.85, 0.45]} />
          <meshStandardMaterial color="#2c3140" roughness={0.75} />
        </mesh>
        <mesh position={[0, 1.38, 0.1]} castShadow>
          <sphereGeometry args={[0.22, 32, 32]} />
          <meshStandardMaterial color="#e8d5c4" roughness={0.55} />
        </mesh>
        <mesh position={[0, 1.22, 0.22]} rotation-x={0.15}>
          <boxGeometry args={[0.5, 0.22, 0.35]} />
          <meshStandardMaterial color="#c8b0a0" roughness={0.6} />
        </mesh>
        {/* Subtle "lip" motion proxy when speaking */}
        <SpeakingJaw active={speaking} />
      </group>
    </>
  )
}

function SpeakingJaw({ active }: { active: boolean }) {
  const jaw = useRef<Mesh>(null)
  useFrame(() => {
    if (!jaw.current) return
    jaw.current.scale.y = active ? 1 + Math.sin(performance.now() / 120) * 0.06 : 1
  })
  return (
    <mesh ref={jaw} position={[0, 1.28, 0.26]}>
      <boxGeometry args={[0.14, 0.04, 0.06]} />
      <meshStandardMaterial color="#a87870" roughness={0.5} />
    </mesh>
  )
}
