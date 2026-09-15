import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import projectIdentity from '../../../../content/project-identity.json'
import { Icon } from '../../shared/components/Icon'

type TravelCity = { id: string; name: string }

export function HomePage() {
  const [cities, setCities] = useState<TravelCity[] | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetch('/api/travel/cities', { signal: controller.signal })
      .then(async response => response.ok ? setCities(await response.json() as TravelCity[]) : setCities([]))
      .catch(error => { if (error.name !== 'AbortError') setCities([]) })
    return () => controller.abort()
  }, [])

  return (
    <main className="home-page" data-testid="home-page">
      <section className="home-start page-container" aria-labelledby="home-title">
        <div className="home-start-heading">
          <div><span className="section-label">YEREL SEYAHAT KATALOĞU</span><h1 id="home-title">Seyahatini planlamaya başla</h1></div>
          <p>Otel veya uçuş ölçütlerini girerek örnek seçenekleri karşılaştır; istersen planını nasıl tarif edeceğini sohbet önizlemesinde gör.</p>
        </div>
        <nav className="start-options" aria-label="Arama türü">
          <Link to="/hotels"><Icon name="hotel" size={22} /><span><strong>Otel ara</strong><small>Konum, tarih ve misafir seç</small></span><Icon name="arrow" size={18} /></Link>
          <Link to="/flights"><Icon name="plane" size={22} /><span><strong>Uçuş ara</strong><small>Rota, tarih ve yolcu seç</small></span><Icon name="arrow" size={18} /></Link>
          <Link to="/chat"><Icon name="chat" size={22} /><span><strong>Planlama örneği</strong><small>Sohbet akışını incele</small></span><Icon name="arrow" size={18} /></Link>
        </nav>
        <div className="home-scope" role="note"><Icon name="info" size={18} /><p><strong>Eğitim amaçlı planlama aracı.</strong> {projectIdentity.serviceDisclaimer}</p></div>
      </section>

      {cities && cities.length > 0 && <section className="destination-section page-container" aria-labelledby="destinations-title">
        <div className="content-heading"><div><span className="section-label">MEVCUT KATALOG</span><h2 id="destinations-title">Desteklenen şehirler</h2></div><p>Veritabanındaki şehirlerden biriyle otel aramasına başla.</p></div>
        <div className="destination-list">{cities.map(city => <Link key={city.id} to={`/hotels?q=${encodeURIComponent(city.name)}`}><Icon name="map-pin" size={18} /><span><strong>{city.name}</strong><small>Otel seçeneklerini ara</small></span><Icon name="arrow" size={16} /></Link>)}</div>
      </section>
      }
    </main>
  )
}
