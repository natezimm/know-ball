import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Avatar } from './avatar';

describe('Avatar', () => {
  let fixture: ComponentFixture<Avatar>;
  const el = () => fixture.nativeElement as HTMLElement;

  async function render(src: string | null, name: string) {
    fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('src', src);
    fixture.componentRef.setInput('name', name);
    await fixture.whenStable();
  }

  it('shows the headshot when there is one', async () => {
    await render('https://heads/1.png', 'Nikola Jokić');

    expect(el().querySelector('img')?.getAttribute('src')).toBe('https://heads/1.png');
  });

  it('falls back to initials when there is no photo', async () => {
    await render(null, 'Shai Gilgeous-Alexander');

    expect(el().querySelector('img')).toBeNull();
    expect(el().textContent?.trim()).toBe('SG');
  });

  it('falls back to initials when the photo fails to load, and tries again for a new photo', async () => {
    await render('https://heads/missing.png', "De'Aaron Fox");
    el().querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();

    expect(el().textContent?.trim()).toBe('DF');

    fixture.componentRef.setInput('src', 'https://heads/2.png');
    await fixture.whenStable();
    expect(el().querySelector('img')).not.toBeNull();
  });
});
