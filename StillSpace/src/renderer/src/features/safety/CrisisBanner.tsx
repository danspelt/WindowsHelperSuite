import { crisisResources } from '../../services/safety/crisisDetection'

type Props = { visible: boolean }

export function CrisisBanner({ visible }: Props) {
  if (!visible) return null
  return (
    <div className="crisis" role="alert">
      <strong>You matter.</strong>
      <p>{crisisResources}</p>
    </div>
  )
}
